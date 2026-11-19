using Microsoft.Web.WebView2.Wpf;
using System.IO;
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

        public async Task<string> GetSelectedTextAsync(WebView2 webView)
        {
            if (webView?.CoreWebView2 == null) return string.Empty;
            string json = await webView.ExecuteScriptAsync("window.getSelection()?.toString() || ''");
            return JsonSerializer.Deserialize<string>(json) ?? string.Empty;
        }

        // Both site extractors explicitly return JSON.stringify(text).
        // Decode WebView2's outer JSON and that inner string exactly once each.
        protected async Task<string> ExecScriptStringAsync(WebView2 webView, string script)
        {
            if (webView?.CoreWebView2 == null) return string.Empty;
            string outer = await webView.ExecuteScriptAsync(script);
            if (string.IsNullOrWhiteSpace(outer) || outer == "null") return string.Empty;
            string? inner = JsonSerializer.Deserialize<string>(outer);
            if (inner == null) return string.Empty;
            using var value = JsonDocument.Parse(inner);
            if (value.RootElement.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("文字列抽出スクリプトがJSON文字列を返しませんでした。");
            return value.RootElement.GetString() ?? string.Empty;
        }

        protected async Task<bool> ExecScriptBoolAsync(WebView2 webView, string script)
        {
            if (webView?.CoreWebView2 == null) return false;
            using var value = JsonDocument.Parse(await webView.ExecuteScriptAsync(script));
            return value.RootElement.ValueKind == JsonValueKind.True;
        }
    }
}
