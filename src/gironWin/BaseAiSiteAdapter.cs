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

        public async Task<string> GetSelectedTextAsync(WebView2 webView)
        {
            if (webView?.CoreWebView2 == null)
            {
                return string.Empty;
            }

            string json = await webView.ExecuteScriptAsync(
                "window.getSelection ? window.getSelection().toString() : ''");

            if (string.IsNullOrWhiteSpace(json) || json == "null")
            {
                return string.Empty;
            }

            try
            {
                return JsonSerializer.Deserialize<string>(json) ?? string.Empty;
            }
            catch
            {
                return json.Trim('"');
            }
        }
    }
}
