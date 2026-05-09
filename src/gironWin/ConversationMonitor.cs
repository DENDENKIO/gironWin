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
    /// 主判定: 最新本文の文字数増加・本文変化が止まったら完了。
    /// 補助判定: MutationObserver による postMessage。
    /// </summary>
    public sealed class ConversationMonitor : IDisposable
    {
        public event EventHandler<GenerationDoneEventArgs>? GenerationDone;

        private readonly IAiSiteAdapter _adapter;
        private readonly WebView2 _webView;
        private bool _disposed;
        private bool _completed;

        private const int PollIntervalMs = 250;
        private const int StableRequiredCount = 2;
        private const int ObserverQuietMs = 600;
        private const int MinMeaningfulLength = 40;

        public ConversationMonitor(IAiSiteAdapter adapter, WebView2 webView)
        {
            _adapter = adapter;
            _webView = webView;
        }

        /// <summary>
        /// 監視を開始し、生成完了テキストを返す。
        /// snapshot は監視開始前に取得した既存テキスト。
        /// </summary>
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
                catch
                {
                }
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
                    try
                    {
                        await PollUntilStableAsync(snapshot, Complete, linkedCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch
                    {
                    }
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

            string lastText = normalizedSnapshot;
            int lastLength = lastText.Length;

            int stableCount = 0;
            bool seenNewText = false;

            while (!ct.IsCancellationRequested)
            {
                string latestText = (await _adapter.ExtractLatestAsync(_webView))?.Trim() ?? string.Empty;
                bool hasNewText = !string.IsNullOrWhiteSpace(latestText) && latestText != normalizedSnapshot;

                if (!hasNewText)
                {
                    stableCount = 0;
                    await Task.Delay(PollIntervalMs, ct);
                    continue;
                }

                seenNewText = true;
                int latestLength = latestText.Length;

                bool changed = latestLength != lastLength || latestText != lastText;

                if (changed)
                {
                    lastText = latestText;
                    lastLength = latestLength;
                    stableCount = 0;
                    await Task.Delay(PollIntervalMs, ct);
                    continue;
                }

                stableCount++;

                // 通常の安定判定
                if (seenNewText && stableCount >= StableRequiredCount)
                {
                    onCompleted(latestText);
                    return;
                }

                // 十分な長さがあり、短時間でも止まったら早期確定
                if (latestLength >= MinMeaningfulLength && stableCount >= 1)
                {
                    onCompleted(latestText);
                    return;
                }

                await Task.Delay(PollIntervalMs, ct);
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

    window.__gironDone = false;
    window.__gironTimer = null;
    const SNAPSHOT = {escapedSnapshot};
    const QUIET_MS = {ObserverQuietMs};

    function getLatestText() {{
        const selectors = [
            'model-response .message-content',
            '.prose',
            '[data-testid=""answer""]',
            '[data-testid=""response""]',
            '[data-response-index]',
            '.markdown'
        ];

        for (const sel of selectors) {{
            const nodes = Array.from(document.querySelectorAll(sel))
                .map(x => (x.innerText || x.textContent || '').trim())
                .filter(x => x.length > 0);
            if (nodes.length > 0) {{
                return nodes[nodes.length - 1];
            }}
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
        }} catch (e) {{}}
    }}

    window.__gironObs = new MutationObserver(() => {{
        const t = getLatestText();
        if (!t || t === SNAPSHOT) return;

        if (window.__gironTimer) clearTimeout(window.__gironTimer);
        window.__gironTimer = setTimeout(() => {{
            const ft = getLatestText();
            if (ft && ft !== SNAPSHOT) {{
                notify(ft);
            }}
        }}, QUIET_MS);
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
