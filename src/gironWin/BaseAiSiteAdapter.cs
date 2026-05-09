using Microsoft.Web.WebView2.Wpf;
using System.Text.Json;
using System.Threading.Tasks;

namespace gironWin
{
    public abstract class BaseAiSiteAdapter : IAiSiteAdapter
    {
        public abstract string SiteName { get; }
        public abstract bool CanHandle(string url);
        public abstract Task<bool> SetInputAsync(WebView2 webView, string text);
        public abstract Task<bool> SendAsync(WebView2 webView);
        public abstract Task<string> ExtractLatestAsync(WebView2 webView);
        public abstract Task<bool> IsGeneratingAsync(WebView2 webView);
        public abstract Task InjectObserverAsync(WebView2 webView);

        // ★ JSON.stringify でラップして返す → 改行・引用符の切り捨て防止
        public async Task<string> GetSelectedTextAsync(WebView2 webView)
        {
            if (webView?.CoreWebView2 == null) return string.Empty;

            string json = await webView.ExecuteScriptAsync(@"
(() => {
    try {
        const sel = window.getSelection();
        if (!sel || sel.rangeCount === 0) return JSON.stringify('');
        return JSON.stringify(sel.toString() || '');
    } catch(e) {
        return JSON.stringify('');
    }
})()");

            if (string.IsNullOrWhiteSpace(json) || json == "null") return string.Empty;
            try { return JsonSerializer.Deserialize<string>(json) ?? string.Empty; }
            catch { return json.Trim('"').Replace("\\n", "\n").Replace("\\\"", "\""); }
        }

        protected async Task<string> ExecScriptStringAsync(WebView2 webView, string script)
        {
            if (webView?.CoreWebView2 == null) return string.Empty;
            string json = await webView.ExecuteScriptAsync(script);
            if (string.IsNullOrWhiteSpace(json) || json == "null") return string.Empty;
            try { return JsonSerializer.Deserialize<string>(json) ?? string.Empty; }
            catch { return json.Trim('"').Replace("\\n", "\n").Replace("\\\"", "\""); }
        }

        protected async Task<bool> ExecScriptBoolAsync(WebView2 webView, string script)
        {
            if (webView?.CoreWebView2 == null) return false;
            string result = await webView.ExecuteScriptAsync(script);
            return result.Contains("true", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
