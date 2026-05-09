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
    ///         StableQuietMs（デフォルト10秒）増減なしで完了と判断。
    /// 補助判定: MutationObserver による postMessage。
    /// </summary>
    public sealed class ConversationMonitor : IDisposable
    {
        public event EventHandler<GenerationDoneEventArgs>? GenerationDone;

        private readonly IAiSiteAdapter _adapter;
        private readonly WebView2 _webView;
        private bool _disposed;
        private bool _completed;

        private const int PollIntervalMs   = 200;    // ポーリング間隔
        private const int StableQuietMs    = 10000;  // ★ 文字数が止まってからの静止確認時間（10秒）
        private const int MinMeaningfulLen = 40;     // 有意テキストの最小文字数
        private const int ObserverQuietMs  = 300;    // MutationObserver の待機時間

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

            string lastText   = normalizedSnapshot;
            int    lastLength = lastText.Length;

            // ★ 文字数が最後に変化した時刻
            DateTime lastChangedAt = DateTime.UtcNow;
            bool     seenNewText   = false;

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(PollIntervalMs, ct);

                string latestText = (await _adapter.ExtractLatestAsync(_webView))?.Trim() ?? string.Empty;
                bool hasNewText   = !string.IsNullOrWhiteSpace(latestText)
                                    && latestText != normalizedSnapshot
                                    && latestText.Length >= MinMeaningfulLen;

                if (!hasNewText)
                {
                    // まだ新テキストが出ていない
                    lastChangedAt = DateTime.UtcNow; // 生成前はタイマーをリセットし続ける
                    continue;
                }

                // ★ 新テキストが出た
                seenNewText = true;

                bool lengthChanged = latestText.Length != lastLength;
                bool textChanged   = latestText != lastText;

                if (lengthChanged || textChanged)
                {
                    // 文字数または内容が変化 → 変化時刻を更新
                    lastText      = latestText;
                    lastLength    = latestText.Length;
                    lastChangedAt = DateTime.UtcNow;
                    continue;
                }

                // ★ 変化なし → 静止時間を計測
                double quietMs = (DateTime.UtcNow - lastChangedAt).TotalMilliseconds;

                if (seenNewText && quietMs >= StableQuietMs)
                {
                    // 10秒間変化がなければ完了
                    onCompleted(latestText);
                    return;
                }

                // まだ静止が足りないのでループ継続
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

        // 文字数が変化したらタイマーリセット
        const len = t.length;
        if (len !== window.__gironLen) {{
            window.__gironLen = len;
            if (window.__gironTimer) clearTimeout(window.__gironTimer);
            window.__gironTimer = null;
        }}

        // タイマーがなければセット（QUIET_MS 後に通知）
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
