using System.Windows;
using System.Windows.Controls;

namespace gironWin
{
    public partial class LogExportOptionsDialog : Window
    {
        public LogExportOptions? Options { get; private set; }

        public LogExportOptionsDialog()
        {
            InitializeComponent();
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var formatText = (FormatComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Markdown";
            var modeText = (ModeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "連結して1ファイル";

            var format = formatText switch
            {
                "HTML" => LogExportFormat.Html,
                "Markdown" => LogExportFormat.Markdown,
                "Text" => LogExportFormat.Text,
                _ => LogExportFormat.Markdown
            };

            var mode = modeText.Contains("個別")
                ? LogExportMode.Separate
                : LogExportMode.Combined;

            Options = new LogExportOptions
            {
                Format = format,
                Mode = mode,
                IncludeMetadata = IncludeMetadataCheckBox.IsChecked == true,
                PreferHtmlSnapshot = PreferHtmlSnapshotCheckBox.IsChecked == true
            };

            DialogResult = true;
            Close();
        }
    }
}
