using Microsoft.Web.WebView2.Wpf;
using System.Threading.Tasks;

namespace gironWin
{
    public interface IAiSiteAdapter
    {
        bool CanHandle(string url);
        Task<bool> SetInputAsync(WebView2 webView, string text);
        Task<bool> SendAsync(WebView2 webView);
        Task<string> GetSelectedTextAsync(WebView2 webView);
        string SiteName { get; }
    }
}
