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

        // ---------------------------------------------------------------
        // 共通: 選択テキスト取得
        // ---------------------------------------------------------------
        public async Task<string> GetSelectedTextAsync(WebView2 webView)
        {
            if (webView?.CoreWebView2 == null)
                return string.Empty;

            string json = await webView.ExecuteScriptAsync(
                "window.getSelection ? window.getSelection().toString() : ''");

            if (string.IsNullOrWhiteSpace(json) || json == "null")
                return string.Empty;

            try { return JsonSerializer.Deserialize<string>(json) ?? string.Empty; }
            catch { return json.Trim('"'); }
        }

        // ---------------------------------------------------------------
        // 共通ヘルパー: JS 実行結果を安全に文字列へ変換
        // ---------------------------------------------------------------
        protected async Task<string> ExecScriptStringAsync(WebView2 webView, string script)
        {
            if (webView?.CoreWebView2 == null)
                return string.Empty;

            string json = await webView.ExecuteScriptAsync(script);
            if (string.IsNullOrWhiteSpace(json) || json == "null")
                return string.Empty;

            try { return JsonSerializer.Deserialize<string>(json) ?? string.Empty; }
            catch { return json.Trim('"'); }
        }

        protected async Task<bool> ExecScriptBoolAsync(WebView2 webView, string script)
        {
            if (webView?.CoreWebView2 == null)
                return false;

            string result = await webView.ExecuteScriptAsync(script);
            return result.Contains("true", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
