using Microsoft.Web.WebView2.Wpf;
using System;
using System.Diagnostics;

namespace gironWin
{
    public partial class MainWindow
    {
        private bool _webViewsReleased;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            BindVisibleLogMenuActions();
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                if (!_webViewsReleased)
                {
                    _webViewsReleased = true;
                    ReleaseBrowser(LeftWebView, "LeftWebView");
                    ReleaseBrowser(RightWebView, "RightWebView");
                }
            }
            finally
            {
                base.OnClosed(e);
            }
        }

        private static void ReleaseBrowser(WebView2? browser, string name)
        {
            if (browser == null) return;
            try
            {
                browser.Dispose();
                Debug.WriteLine($"[Lifecycle] Released {name}.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Lifecycle] Failed to release {name}: {ex}");
            }
        }
    }
}
