using System.Windows;

namespace gironWin
{
    public partial class App
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Hidden auxiliary windows must not keep the application running.
            ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
            base.OnStartup(e);
        }
    }
}
