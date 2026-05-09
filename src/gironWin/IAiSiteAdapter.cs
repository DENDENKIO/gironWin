using Microsoft.Web.WebView2.Wpf;
using System.Threading.Tasks;

namespace gironWin
{
    public interface IAiSiteAdapter
    {
        string SiteName { get; }
        bool CanHandle(string url);

        // --- 既存 ---
        Task<bool> SetInputAsync(WebView2 webView, string text);
        Task<bool> SendAsync(WebView2 webView);
        Task<string> GetSelectedTextAsync(WebView2 webView);

        // --- Phase 2 追加 ---
        /// <summary>最新の AI 応答メッセージを取得する。</summary>
        Task<string> ExtractLatestAsync(WebView2 webView);

        /// <summary>現在 AI が生成中かどうかを判定する。</summary>
        Task<bool> IsGeneratingAsync(WebView2 webView);

        /// <summary>
        /// MutationObserver 監視スクリプトを注入する。
        /// 生成完了を検知したら chrome.webview.postMessage で通知する。
        /// </summary>
        Task InjectObserverAsync(WebView2 webView);
    }
}
