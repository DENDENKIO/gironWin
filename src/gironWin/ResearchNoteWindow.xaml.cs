using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace gironWin
{
    public partial class ResearchNoteWindow : Window
    {
        private readonly ResearchModeService _service;
        private IReadOnlyList<ResearchTagEntry> _allEntries = new List<ResearchTagEntry>();

        public ResearchNoteWindow(ResearchModeService service)
        {
            InitializeComponent();
            _service = service;

            // Loaded 後に初回描画（コントロールが確実に生成されてから）
            Loaded += (_, _) =>
            {
                if (TagGrid != null)
                    TagGrid.SelectionChanged += TagGrid_SelectionChanged;
                Refresh();
            };
        }

        // ---------------------------------------------------------------
        // 表示更新
        // ---------------------------------------------------------------

        public void Refresh()
        {
            if (_service == null) return;
            // TagGrid がまだ存在しない場合は何もしない
            if (TagGrid == null) return;

            _allEntries = _service.Entries;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            // コントロール null ガード
            if (TagGrid == null || CountLabel == null) return;

            string tagFilter = (TagFilterCombo?.SelectedItem as ComboBoxItem)
                                   ?.Content?.ToString() ?? "すべて";
            string importanceFilter = (ImportanceFilterCombo?.SelectedItem as ComboBoxItem)
                                          ?.Content?.ToString() ?? "すべて";

            var filtered = _allEntries.AsEnumerable();

            if (tagFilter != "すべて")
                filtered = filtered.Where(e => e.TagType == tagFilter || e.SubTagType == tagFilter);

            if (importanceFilter != "すべて")
            {
                int imp = importanceFilter.Contains("高") ? 3
                         : importanceFilter.Contains("中") ? 2
                         : 1;
                filtered = filtered.Where(e => e.Importance == imp);
            }

            var list = filtered
                .OrderByDescending(e => e.Importance)
                .ThenBy(e => e.TurnNumber)
                .ToList();

            TagGrid.ItemsSource = list;
            CountLabel.Text     = $"{list.Count} 件";
        }

        // ---------------------------------------------------------------
        // イベント
        // ---------------------------------------------------------------

        private void Filter_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilter();

        private void TagGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TagGrid?.SelectedItem is ResearchTagEntry entry)
                DetailBox.Text = $"[{entry.DisplayTag}] Turn {entry.TurnNumber} | {entry.ImportanceLabel}\n{entry.Text}";
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (TagGrid?.SelectedItem is ResearchTagEntry entry)
                Clipboard.SetText(entry.Text);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
