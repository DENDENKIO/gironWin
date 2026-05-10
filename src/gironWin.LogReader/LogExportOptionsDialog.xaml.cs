// src/gironWin.LogReader/LogExportOptionsDialog.xaml.cs
using System.Windows;
using System.Windows.Controls;

namespace gironWin.LogReader
{
    public partial class LogExportOptionsDialog : Window
    {
        public LogExportOptions? Options { get; private set; }

        public LogExportOptionsDialog()
        {
            InitializeComponent();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var fmt = (FormatCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Markdown";
            var mod = (ModeCombo.SelectedItem  as ComboBoxItem)?.Content?.ToString() ?? "連結して1ファイル";

            Options = new LogExportOptions
            {
                Format             = fmt switch { "HTML" => LogExportFormat.Html, "Text" => LogExportFormat.Text, _ => LogExportFormat.Markdown },
                Mode               = mod.Contains("個別") ? LogExportMode.Separate : LogExportMode.Combined,
                IncludeMetadata    = MetaCheck.IsChecked  == true,
                PreferHtmlSnapshot = SnapCheck.IsChecked  == true
            };

            DialogResult = true;
        }
    }
}
