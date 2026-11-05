using Microsoft.Web.WebView2.Wpf;
using System;
using System.Diagnostics;

namespace gironWin.LogReader
{
    public partial class MainWindow
    {
        private bool _webViewsReleased;

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                // Mark closed before Dispose can trigger callbacks.
                _readerClosing = true;
                _requestSerial++;
                _selectionRevision = -1;
                _bodyRenderedRevision = -1;
                _webViewReady = false;
                if (!_webViewsReleased)
                {
                    _webViewsReleased = true;
                    ReleaseBrowser(BodyWebView, "BodyWebView");
                    ReleaseBrowser(AiHtmlWebView, "AiHtmlWebView");
                    ReleaseBrowser(ImageListWebView, "ImageListWebView");
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
                Debug.WriteLine($"[LogReader Lifecycle] Released {name}.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LogReader Lifecycle] Failed to release {name}: {ex}");
            }
        }
    }
}
