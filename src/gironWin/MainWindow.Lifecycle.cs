using Microsoft.Web.WebView2.Wpf;
using System;
using System.Diagnostics;

namespace gironWin
{
    public partial class MainWindow
    {
        private bool _webViewsReleased;

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                // Window_Closing already requests cancellation of the debate.
                // Release each control even if releasing another one fails.
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
