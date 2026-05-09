using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace gironWin
{
    /// <summary>
    /// AI 応答の生成完了を監視する。
    /// 主判定: テキスト文字数をリアルタイム監視し、
    ///         StableQuietMs（デフォルト2秒）増減なしで完了と判断。
    ///         または IsGeneratingAsync が false になった瞬間を検知。
    /// </summary>
    public sealed class ConversationMonitor : IDisposable
    {
        public event EventHandler<GenerationDoneEventArgs>? GenerationDone;

        private readonly IAiSiteAdapter _adapter;
        private readonly WebView2 _webView;
        private bool _disposed;
        private bool _completed;

        private const int PollIntervalMs   = 150;   // 検出頻度アップ
        private const int StableQuietMs    = 2000;  // 10秒→2秒に短縮
        private const int MinMeaningfulLen = 20;    // 短い回答も拾う
        private const int ObserverQuietMs  = 500;   // 安定性重視
        private const int AfterStopWaitMs  = 800;   // 生成停止後の描画待ち

        public ConversationMonitor(IAiSiteAdapter adapter, WebView2 webView)
        {
            _adapter = adapter;
            _webView = webView;
        }

        public async Task<string> WaitForCompletionAsync(
            string snapshot,
            int timeoutMs,
            CancellationToken ct = default)
        {
            if (_webView?.CoreWebView2 == null)
                return string.Empty;

            _completed = false;

            var tcs = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            void Complete(string text)
            {
                if (_completed) return;
                if (string.IsNullOrWhiteSpace(text)) return;
                _completed = true;
                tcs.TrySetResult(text);
                GenerationDone?.Invoke(this, new GenerationDoneEventArgs(_adapter.SiteName, text));
            }

            void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
            {
                if (_completed) return;
                try
                {
                    string raw = e.TryGetWebMessageAsString();
                    var payload = JsonSerializer.Deserialize<WebMessagePayload>(raw);
                    if (payload?.Type == "GenerationDone" && !string.IsNullOrWhiteSpace(payload.Text))
                    {
                        string text = payload.Text.Trim();
                        if (text != (snapshot ?? string.Empty).Trim())
                            Complete(text);
                    }
                }
                catch { }
            }

            _webView.CoreWebView2.WebMessageReceived += OnMessage;

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(timeoutMs);
            linkedCts.Token.Register(() => tcs.TrySetCanceled());

            try
            {
                await InjectObserverAsync(snapshot);

                _ = Task.Run(async () =>
                {
                    try { await PollUntilStableAsync(snapshot, Complete, linkedCts.Token); }
                    catch (OperationCanceledException) { }
                    catch { }
                }, linkedCts.Token);

                return await tcs.Task;
            }
            catch (OperationCanceledException)
            {
                string fallback = (await _adapter.ExtractLatestAsync(_webView))?.Trim() ?? string.Empty;
                string normalizedSnapshot = (snapshot ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(fallback) && fallback != normalizedSnapshot)
                    return fallback;
                return string.Empty;
            }
            finally
            {
                if (_webView?.CoreWebView2 != null)
                    _webView.CoreWebView2.WebMessageReceived -= OnMessage;
            }
        }

        private async Task PollUntilStableAsync(
            string snapshot,
            Action<string> onCompleted,
            CancellationToken ct)
        {
            string normalizedSnapshot = (snapshot ?? string.Empty).Trim();

            string lastText    = normalizedSnapshot;
            int    lastLength  = lastText.Length;
            DateTime lastChangedAt = DateTime.UtcNow;
            bool seenNewText   = false;
            bool wasGenerating = false;

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(PollIntervalMs, ct);

                // ★ IsGeneratingAsync() で生成中フラグを確認
                bool isGenerating = false;
                try { isGenerating = await _adapter.IsGeneratingAsync(_webView); } catch { }

                string latestText = (await _adapter.ExtractLatestAsync(_webView))?.Trim() ?? string.Empty;
                bool hasNewText   = !string.IsNullOrWhiteSpace(latestText)
                                    && latestText != normalizedSnapshot
                                    && latestText.Length >= MinMeaningfulLen;

                if (!hasNewText)
                {
                    lastChangedAt = DateTime.UtcNow;
                    if (isGenerating) wasGenerating = true;
                    continue;
                }

                seenNewText = true;
                if (isGenerating) wasGenerating = true;

                bool lengthChanged = latestText.Length != lastLength;
                bool textChanged   = latestText != lastText;

                if (lengthChanged || textChanged)
                {
                    lastText      = latestText;
                    lastLength    = latestText.Length;
                    lastChangedAt = DateTime.UtcNow;
                    continue;
                }

                double quietMs = (DateTime.UtcNow - lastChangedAt).TotalMilliseconds;

                // ★ パターン1: IsGenerating が true→false に変わった直後
                if (wasGenerating && !isGenerating && seenNewText)
                {
                    // 少し待ってから取得（描画の遅延を吸収）
                    await Task.Delay(AfterStopWaitMs, ct);
                    string finalText = (await _adapter.ExtractLatestAsync(_webView))?.Trim() ?? latestText;
                    if (!string.IsNullOrWhiteSpace(finalText) && finalText != normalizedSnapshot)
                    {
                        onCompleted(finalText);
                        return;
                    }
                }

                // ★ パターン2: StableQuietMs（2秒）テキスト変化なし
                if (seenNewText && quietMs >= StableQuietMs)
                {
                    onCompleted(latestText);
                    return;
                }
            }
        }

        private async Task InjectObserverAsync(string snapshot)
        {
            string escapedSnapshot = JsonSerializer.Serialize(snapshot ?? string.Empty);
            string siteName = _adapter.SiteName;

            string script = $@"
(() => {{
    if (window.__gironObs) {{
        window.__gironObs.disconnect();
        window.__gironObs = null;
    }}

    window.__gironDone   = false;
    window.__gironTimer  = null;
    window.__gironLen    = 0;

    const SNAPSHOT = {escapedSnapshot};
    const QUIET_MS  = {ObserverQuietMs};

    function getLatestText() {{
        const selectors = [
            'div[id^=""markdown-content-""]',
            'model-response .message-content',
            '.prose',
            '[data-testid=""answer""]',
            '[data-testid=""response""]',
            '[data-response-index]',
            '.markdown'
        ];
        for (const sel of selectors) {{
            const nodes = Array.from(document.querySelectorAll(sel))
                .filter(el => {{
                    const s = window.getComputedStyle(el);
                    return s.display !== 'none' && s.visibility !== 'hidden';
                }})
                .map(x => (x.innerText || x.textContent || '').trim())
                .filter(x => x.length > 0);
            if (nodes.length > 0) return nodes[nodes.length - 1];
        }}
        return '';
    }}

    function notify(text) {{
        if (window.__gironDone) return;
        if (!text || text === SNAPSHOT) return;
        window.__gironDone = true;
        try {{
            chrome.webview.postMessage(JSON.stringify({{
                type: 'GenerationDone',
                text: text,
                site: '{siteName}'
            }}));
        }} catch(e) {{}}
    }}

    window.__gironObs = new MutationObserver(() => {{
        const t = getLatestText();
        if (!t || t === SNAPSHOT) return;

        const len = t.length;
        if (len !== window.__gironLen) {{
            window.__gironLen = len;
            if (window.__gironTimer) clearTimeout(window.__gironTimer);
            window.__gironTimer = null;
        }}

        if (!window.__gironTimer) {{
            window.__gironTimer = setTimeout(() => {{
                const ft = getLatestText();
                if (ft && ft !== SNAPSHOT) notify(ft);
            }}, QUIET_MS);
        }}
    }});

    window.__gironObs.observe(document.body, {{
        childList: true,
        subtree: true,
        characterData: true
    }});
}})();";

            await _webView.ExecuteScriptAsync(script);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }

        private sealed class WebMessagePayload
        {
            public string? Type { get; set; }
            public string? Text { get; set; }
            public string? Site { get; set; }
        }
    }

    public sealed class GenerationDoneEventArgs : EventArgs
    {
        public string SiteName { get; }
        public string Text { get; }

        public GenerationDoneEventArgs(string siteName, string text)
        {
            SiteName = siteName;
            Text = text;
        }
    }
}
