using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace gironWin
{
    /// <summary>
    /// AI の生成完了を検知する。
    /// 注入時点のスナップショットと比較し、新しいテキストが安定してから通知する。
    /// </summary>
    public class ConversationMonitor : IDisposable
    {
        public event EventHandler<GenerationDoneEventArgs>? GenerationDone;

        private readonly IAiSiteAdapter _adapter;
        private readonly WebView2 _webView;
        private bool _disposed;
        private bool _notified;

        public ConversationMonitor(IAiSiteAdapter adapter, WebView2 webView)
        {
            _adapter = adapter;
            _webView = webView;
        }

        /// <summary>
        /// 監視を開始する。
        /// snapshot: 監視開始直前の「既存テキスト」。これと異なる新テキストが来たら通知。
        /// </summary>
        public async Task StartWatchingAsync(string snapshot, CancellationToken ct = default)
        {
            if (_webView?.CoreWebView2 == null) return;

            _notified = false;
            string escapedSnapshot = JsonSerializer.Serialize(snapshot);

            // WebMessageReceived を登録
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            ct.Register(() => _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived);

            // スナップショット付きで Observer 注入
            await InjectObserverWithSnapshotAsync(escapedSnapshot);
        }

        private async Task InjectObserverWithSnapshotAsync(string escapedSnapshot)
        {
            string adapterScript = _adapter.SiteName switch
            {
                "Gemini" => @"
                    Array.from(document.querySelectorAll('model-response .message-content'))
                         .pop()?.innerText?.trim() ?? ''",

                "Perplexity" => @"
                    Array.from(document.querySelectorAll('.prose'))
                         .pop()?.innerText?.trim()
                    ?? Array.from(document.querySelectorAll('[data-testid=""answer""]'))
                         .pop()?.innerText?.trim()
                    ?? ''",

                _ => @"
                    Array.from(document.querySelectorAll('.prose, model-response .message-content, [data-testid=""answer""]'))
                         .pop()?.innerText?.trim() ?? ''"
            };

            string script = $@"
(() => {{
    // 既存の Observer をリセット
    if (window.__gironObserver) {{
        window.__gironObserver.disconnect();
        window.__gironObserver = null;
    }}
    window.__gironNotified = false;
    window.__gironQuietTimer = null;
    const QUIET_MS = 2000;
    const SNAPSHOT = {escapedSnapshot};

    function getLatestText() {{
        return {adapterScript};
    }}

    function notifyDone(text) {{
        if (window.__gironNotified) return;
        window.__gironNotified = true;
        window.__gironObserver?.disconnect();
        chrome.webview.postMessage(JSON.stringify({{
            type: 'GenerationDone',
            text: text,
            site: '{_adapter.SiteName}'
        }}));
    }}

    window.__gironObserver = new MutationObserver(() => {{
        const text = getLatestText();

        // スナップショットと同じなら無視（古いテキスト）
        if (!text || text === SNAPSHOT) return;

        // 生成中インジケータが残っている間は通知しない
        const isGenerating = !!document.querySelector(
            'button[aria-label*=""Stop""], button[aria-label*=""停止""], ' +
            '.animate-pulse, [data-generating=""true""]'
        );
        if (isGenerating) return;

        // テキストが QUIET_MS の間変化しなければ完了とみなす
        if (window.__gironQuietTimer) clearTimeout(window.__gironQuietTimer);
        window.__gironQuietTimer = setTimeout(() => {{
            const finalText = getLatestText();
            if (finalText && finalText !== SNAPSHOT) notifyDone(finalText);
        }}, QUIET_MS);
    }});

    window.__gironObserver.observe(document.body, {{
        childList: true, subtree: true, characterData: true
    }});
}})();";

            await _webView.ExecuteScriptAsync(script);
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (_notified) return;
            try
            {
                string raw = e.TryGetWebMessageAsString();
                var doc = JsonSerializer.Deserialize<WebMessagePayload>(raw);
                if (doc?.Type == "GenerationDone")
                {
                    _notified = true;
                    _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                    GenerationDone?.Invoke(this, new GenerationDoneEventArgs(
                        doc.Site ?? _adapter.SiteName,
                        doc.Text ?? string.Empty));
                }
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_webView?.CoreWebView2 != null)
                _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
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
