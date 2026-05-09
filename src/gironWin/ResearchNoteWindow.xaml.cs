using System.Windows;

namespace gironWin
{
    public partial class ResearchNoteWindow : Window
    {
        private readonly ResearchModeService _service;

        public ResearchNoteWindow(ResearchModeService service)
        {
            InitializeComponent();
            _service = service;
            Refresh();
        }

        private void Refresh()
        {
            bool unverifiedOnly = ShowUnverifiedOnlyCheckBox.IsChecked == true;
            if (unverifiedOnly)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("# 未検証・要確認一覧");
                sb.AppendLine();
                foreach (var e in _service.GetUnverified())
                    sb.AppendLine($"- [{e.TagType}] {e.Label}: {e.Content}");
                NoteTextBox.Text = sb.ToString();
            }
            else
            {
                NoteTextBox.Text = _service.BuildResearchNote();
            }
        }

        private void Filter_Changed(object sender, RoutedEventArgs e) => Refresh();

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(NoteTextBox.Text);
            MessageBox.Show("研究ノートをコピーしました。", "コピー完了",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
