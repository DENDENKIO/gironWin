using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace gironWin
{
    /// <summary>
    /// WebView2 の WebMessageReceived を監視し、
    /// AI の生成完了通知（GenerationDone）を C# イベントとして上げる。
    /// </summary>
    public class ConversationMonitor
    {
        public event EventHandler<GenerationDoneEventArgs>? GenerationDone;

        private readonly IAiSiteAdapter _adapter;
        private readonly WebView2 _webView;
        private bool _isWatching;

        public ConversationMonitor(IAiSiteAdapter adapter, WebView2 webView)
        {
            _adapter = adapter;
            _webView = webView;
        }

        /// <summary>
        /// 監視スクリプトを注入し、次の生成完了を待機する。
        /// </summary>
        public async Task StartWatchingAsync()
        {
            if (_isWatching) return;
            _isWatching = true;

            _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            await _adapter.InjectObserverAsync(_webView);
        }

        public void StopWatching()
        {
            _isWatching = false;
            if (_webView?.CoreWebView2 != null)
                _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string raw = e.TryGetWebMessageAsString();
                var msg = JsonSerializer.Deserialize<WebMessagePayload>(raw);

                if (msg?.Type == "GenerationDone")
                {
                    _isWatching = false;
                    _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                    GenerationDone?.Invoke(this, new GenerationDoneEventArgs(
                        msg.Site ?? _adapter.SiteName,
                        msg.Text ?? string.Empty));
                }
            }
            catch { /* 解析失敗は無視 */ }
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
