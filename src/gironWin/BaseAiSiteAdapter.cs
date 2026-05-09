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
        // ★ 修正: JSON.stringify でラップして返す → 改行・引用符の切り捨てを防ぐ
        // ---------------------------------------------------------------
        public async Task<string> GetSelectedTextAsync(WebView2 webView)
        {
            if (webView?.CoreWebView2 == null)
                return string.Empty;

            // JSON.stringify でラップすることで改行・特殊文字を安全にエスケープ
            // window.getSelection() が null の場合も空文字列として返す
            string json = await webView.ExecuteScriptAsync(@"
(() => {
    try {
        const sel = window.getSelection();
        if (!sel || sel.rangeCount === 0) return JSON.stringify('');
        const text = sel.toString();
        return JSON.stringify(text || '');
    } catch(e) {
        return JSON.stringify('');
    }
})()");

            if (string.IsNullOrWhiteSpace(json) || json == "null")
                return string.Empty;

            try
            {
                return JsonSerializer.Deserialize<string>(json) ?? string.Empty;
            }
            catch
            {
                // Deserialize 失敗時のフォールバック（前後の引用符を消す）
                return json.Trim('"').Replace("\\n", "\n").Replace("\\\"", "\"");
            }
        }

        // ---------------------------------------------------------------
        // 共通ヘルパー: JS 実行結果を安全に文字列へ変換
        // ★ 修正: JS 側で JSON.stringify してから返す
        // ---------------------------------------------------------------
        protected async Task<string> ExecScriptStringAsync(WebView2 webView, string script)
        {
            if (webView?.CoreWebView2 == null)
                return string.Empty;

            // script が既に JSON.stringify(...) を返す想定
            string json = await webView.ExecuteScriptAsync(script);
            if (string.IsNullOrWhiteSpace(json) || json == "null")
                return string.Empty;

            try
            {
                return JsonSerializer.Deserialize<string>(json) ?? string.Empty;
            }
            catch
            {
                return json.Trim('"').Replace("\\n", "\n").Replace("\\\"", "\"");
            }
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
