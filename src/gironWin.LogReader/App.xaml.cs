using System;
using System.Windows;

namespace gironWin.LogReader
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            string jsonPath = e.Args.Length >= 1 ? e.Args[0] : string.Empty;
            var window = new MainWindow(jsonPath);
            window.Show();
        }
    }
}
