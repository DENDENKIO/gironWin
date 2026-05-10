using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace gironWin
{
    public partial class DebugLogWindow : Window
    {
        private readonly System.Collections.Generic.List<AppLogEntry> _allLogs      = new();
        private readonly ObservableCollection<AppLogEntry>            _filteredLogs = new();

        private int _errorCount = 0;
        private int _warnCount  = 0;
        private int _turnCount  = 0;
        private int _roundCount = 0;

        public DebugLogWindow()
        {
            InitializeComponent();
            LogListBox.ItemsSource = _filteredLogs;

            AppLogger.EntryAdded += OnEntryAdded;

            foreach (var entry in AppLogger.GetBuffer())
                AcceptEntry(entry, applyFilter: true);

            UpdateStatusBar();
        }

        // ---------------------------------------------------------------
        // AppLogger コールバック
        // ---------------------------------------------------------------

        private void OnEntryAdded(object? sender, AppLogEntry entry)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => OnEntryAdded(sender, entry));
                return;
            }
            AcceptEntry(entry, applyFilter: true);
        }

        private void AcceptEntry(AppLogEntry entry, bool applyFilter)
        {
            _allLogs.Add(entry);

            if (entry.Level == LogLevel.Error) _errorCount++;
            if (entry.Level == LogLevel.Warn)  _warnCount++;
            if (entry.Category == LogCategory.Turn)
            {
                if (entry.Message.Contains("開始")) _turnCount++;
                if (entry.Message.Contains("leftCount") && entry.Message.Contains("rightCount"))
                    ParseRoundCount(entry.Message);
            }

            if (applyFilter && MatchesFilter(entry))
            {
                _filteredLogs.Add(entry);
                if (AutoScrollCheckBox?.IsChecked == true && _filteredLogs.Count > 0)
                    LogListBox?.ScrollIntoView(_filteredLogs[_filteredLogs.Count - 1]);
            }

            UpdateStatusBar();
        }

        private void ParseRoundCount(string msg)
        {
            var m = System.Text.RegularExpressions.Regex.Match(msg, @"rightCount=(\d+)");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int rc))
                _roundCount = rc;
        }

        // ---------------------------------------------------------------
        // フィルタ
        // ---------------------------------------------------------------

        private bool MatchesFilter(AppLogEntry entry)
        {
            if (ChkDebug == null) return true;

            if (entry.Level == LogLevel.Debug && ChkDebug.IsChecked  != true) return false;
            if (entry.Level == LogLevel.Info  && ChkInfo.IsChecked   != true) return false;
            if (entry.Level == LogLevel.Warn  && ChkWarn.IsChecked   != true) return false;
            if (entry.Level == LogLevel.Error && ChkError.IsChecked  != true) return false;

            bool catOk = entry.Category switch
            {
                LogCategory.RunLoop  => ChkRunLoop.IsChecked  == true,
                LogCategory.Turn     => ChkTurn.IsChecked     == true,
                LogCategory.Monitor  => ChkMonitor.IsChecked  == true,
                LogCategory.Transfer => ChkTransfer.IsChecked == true,
                LogCategory.Adapter  => ChkAdapter.IsChecked  == true,
                LogCategory.Approval => ChkApproval.IsChecked == true,
                LogCategory.Session  => ChkSession.IsChecked  == true,
                LogCategory.Research => ChkResearch.IsChecked == true,
                LogCategory.System   => ChkSystem.IsChecked   == true,
                _                    => true
            };
            if (!catOk) return false;

            string keyword = FilterTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(keyword) &&
                !entry.FormattedLine.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private void Filter_Changed(object sender, RoutedEventArgs e)  => RebuildFilteredList();
        private void FilterTextBox_TextChanged(object s, TextChangedEventArgs e) => RebuildFilteredList();

        private void RebuildFilteredList()
        {
            _filteredLogs.Clear();
            foreach (var entry in _allLogs)
                if (MatchesFilter(entry))
                    _filteredLogs.Add(entry);
            UpdateStatusBar();
        }

        // ---------------------------------------------------------------
        // ステータスバー
        // ---------------------------------------------------------------

        private void UpdateStatusBar()
        {
            if (TotalLinesLabel == null) return;
            TotalLinesLabel.Text = $"ログ: {_allLogs.Count} 行";
            FilteredLabel.Text   = $"表示: {_filteredLogs.Count} 行";
            ErrorCountLabel.Text = $"ERROR: {_errorCount}";
            WarnCountLabel.Text  = $"WARN: {_warnCount}";
            TurnCountLabel.Text  = $"ターン: {_turnCount}";
            RoundCountLabel.Text = $"往復: {_roundCount}";
        }

        // ---------------------------------------------------------------
        // ボタンイベント
        // ---------------------------------------------------------------

        /// <summary>全ログをクリップボードにコピー</summary>
        private void CopyAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_allLogs.Count == 0) return;
            var sb = new StringBuilder();
            foreach (var entry in _allLogs)
                sb.AppendLine(entry.FormattedLine);
            SetClipboardSafe(sb.ToString());
            ShowCopyResult($"全 {_allLogs.Count} 行をコピーしました。");
        }

        /// <summary>フィルタ後の表示行をクリップボードにコピー</summary>
        private void CopyFilteredButton_Click(object sender, RoutedEventArgs e)
        {
            if (_filteredLogs.Count == 0) return;
            var sb = new StringBuilder();
            foreach (var entry in _filteredLogs)
                sb.AppendLine(entry.FormattedLine);
            SetClipboardSafe(sb.ToString());
            ShowCopyResult($"表示中 {_filteredLogs.Count} 行をコピーしました。");
        }

        /// <summary>選択行のみクリップボードにコピー</summary>
        private void CopySelectedButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = LogListBox.SelectedItems
                .Cast<AppLogEntry>()
                .ToList();
            if (selected.Count == 0)
            {
                ShowCopyResult("行を選択してからコピーしてください。（Shift/Ctrl クリックで複数選択可）");
                return;
            }
            var sb = new StringBuilder();
            foreach (var entry in selected)
                sb.AppendLine(entry.FormattedLine);
            SetClipboardSafe(sb.ToString());
            ShowCopyResult($"選択 {selected.Count} 行をコピーしました。");
        }

        private static void SetClipboardSafe(string text)
        {
            try   { Clipboard.SetText(text); }
            catch { /* クリップボードロック時は無視 */ }
        }

        private void ShowCopyResult(string msg)
        {
            // ステータスバーの FilteredLabel を一時的にメッセージ表示
            FilteredLabel.Text = msg;
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                UpdateStatusBar();
            };
            timer.Start();
        }

        /// <summary>クリア</summary>
        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            AppLogger.Clear();
            _allLogs.Clear();
            _filteredLogs.Clear();
            _errorCount = _warnCount = _turnCount = _roundCount = 0;
            UpdateStatusBar();
        }

        /// <summary>表示行をファイル保存</summary>
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter   = "ログファイル|*.log|テキスト|*.txt",
                FileName = $"debug_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            if (dlg.ShowDialog() != true) return;

            var sb = new StringBuilder();
            foreach (var entry in _filteredLogs)
                sb.AppendLine(entry.FormattedLine);
            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        protected override void OnClosed(EventArgs e)
        {
            AppLogger.EntryAdded -= OnEntryAdded;
            base.OnClosed(e);
        }
    }
}
