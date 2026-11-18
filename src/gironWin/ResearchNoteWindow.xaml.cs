using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace gironWin
{
    public partial class ResearchNoteWindow : Window
    {
        private readonly ResearchModeService _service;
        private readonly DispatcherTimer _refreshTimer;
        private IReadOnlyList<ResearchTagEntry> _allEntries = new List<ResearchTagEntry>();
        private bool _refreshing;
        private bool _closed;

        public ResearchNoteWindow(ResearchModeService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            InitializeComponent();
            _refreshTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _refreshTimer.Tick += RefreshTimer_Tick;
            Loaded += Window_Loaded;
            Unloaded += Window_Unloaded;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (_closed) return;
            TagGrid.SelectionChanged -= TagGrid_SelectionChanged;
            TagGrid.SelectionChanged += TagGrid_SelectionChanged;
            Refresh();
            _refreshTimer.Start();
        }

        private void Window_Unloaded(object sender, RoutedEventArgs e)
            => _refreshTimer.Stop();

        private void RefreshTimer_Tick(object? sender, EventArgs e)
        {
            if (!_closed && IsVisible && _service.Entries.Count != _allEntries.Count)
                Refresh();
        }

        public void Refresh()
        {
            if (_closed || TagGrid == null) return;
            _allEntries = _service.Entries.ToList();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (_closed || TagGrid == null || CountLabel == null) return;
            var selected = TagGrid.SelectedItem as ResearchTagEntry;
            string tagFilter = (TagFilterCombo?.SelectedItem as ComboBoxItem)
                ?.Content?.ToString() ?? "すべて";
            string importanceFilter = (ImportanceFilterCombo?.SelectedItem as ComboBoxItem)
                ?.Content?.ToString() ?? "すべて";
            var filtered = _allEntries.AsEnumerable();
            if (tagFilter != "すべて")
                filtered = filtered.Where(entry => entry.TagType == tagFilter || entry.SubTagType == tagFilter);
            if (importanceFilter != "すべて")
            {
                int importance = importanceFilter.Contains("高") ? 3
                    : importanceFilter.Contains("中") ? 2 : 1;
                filtered = filtered.Where(entry => entry.Importance == importance);
            }
            var list = filtered.OrderByDescending(entry => entry.Importance)
                .ThenBy(entry => entry.TurnNumber).ToList();
            _refreshing = true;
            try
            {
                TagGrid.ItemsSource = list;
                TagGrid.SelectedItem = selected != null && list.Contains(selected) ? selected : null;
                CountLabel.Text = $"{list.Count} 件";
            }
            finally { _refreshing = false; }
            UpdateDetail();
        }

        private void Filter_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilter();

        private void TagGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_refreshing) UpdateDetail();
        }

        private void UpdateDetail()
        {
            if (DetailBox == null) return;
            DetailBox.Text = TagGrid?.SelectedItem is ResearchTagEntry entry
                ? $"[{entry.DisplayTag}] Turn {entry.TurnNumber} | {entry.ImportanceLabel}\n{entry.Text}"
                : string.Empty;
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (TagGrid?.SelectedItem is not ResearchTagEntry entry) return;
            try { Clipboard.SetText(entry.Text); }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"コピーできませんでした。\n{ex.Message}",
                    "研究ノート", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            _closed = true;
            _refreshTimer.Stop();
            _refreshTimer.Tick -= RefreshTimer_Tick;
            Loaded -= Window_Loaded;
            Unloaded -= Window_Unloaded;
            TagGrid.SelectionChanged -= TagGrid_SelectionChanged;
            base.OnClosed(e);
        }
    }
}
