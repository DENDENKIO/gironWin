using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace gironWin
{
    public partial class ResearchNoteWindow : Window
    {
        private readonly ResearchService _researchService;
        private List<ResearchTagEntry>   _allEntries = new();

        public ResearchNoteWindow(ResearchService researchService)
        {
            InitializeComponent();
            _researchService = researchService;
            Refresh();
        }

        private void Refresh()
        {
            _allEntries = _researchService.Entries.ToList();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string filter =
                (TagFilterCombo.SelectedItem as ComboBoxItem)?.Content?.ToString()
                ?? "\u3059\u3079\u3066";

            var filtered = filter == "\u3059\u3079\u3066"
                ? _allEntries
                : _allEntries.Where(e => e.TagType == filter).ToList();

            TagGrid.ItemsSource = filtered;
            CountLabel.Text     = $"{filtered.Count} \u4ef6";
        }

        private void TagFilterCombo_SelectionChanged(
            object sender, SelectionChangedEventArgs e) => ApplyFilter();

        private void CopyMarkdownButton_Click(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# \u7814\u7a76\u30ce\u30fc\u30c8");
            sb.AppendLine();

            foreach (var group in _allEntries.GroupBy(e => e.TagType))
            {
                sb.AppendLine($"## {group.Key}");
                foreach (var entry in group)
                    sb.AppendLine($"- **Turn {entry.TurnNumber}**: {entry.Content}");
                sb.AppendLine();
            }

            Clipboard.SetText(sb.ToString());
            MessageBox.Show("\u30af\u30ea\u30c3\u30d7\u30dc\u30fc\u30c9\u306b\u30b3\u30d4\u30fc\u3057\u307e\u3057\u305f\u3002",
                "\u7814\u7a76\u30ce\u30fc\u30c8", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
