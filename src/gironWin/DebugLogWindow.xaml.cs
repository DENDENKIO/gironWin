using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace gironWin
{
    /// <summary>
    /// デバッグログエントリ（1行分）
    /// </summary>
    public class DebugLogEntry
    {
        public string Message { get; set; } = string.Empty;
        /// <summary>roundCount ログ（緑表示）</summary>
        public bool IsRound => Message.Contains("roundCount") || Message.Contains("往復");
        /// <summary>エラー・失敗ログ（赤表示）</summary>
        public bool IsError => Message.Contains("失敗") || Message.Contains("未検出") || Message.Contains("停止");
        /// <summary>終了ログ（黄表示）</summary>
        public bool IsEnd   => Message.Contains("終了") || Message.Contains("MaxTurns");
    }

    public partial class DebugLogWindow : Window
    {
        // 全ログ（フィルタ前）
        private readonly System.Collections.Generic.List<DebugLogEntry> _allLogs = new();
        // フィルタ後に ListBox へバインドするコレクション
        private readonly ObservableCollection<DebugLogEntry> _filteredLogs = new();

        private int _roundCount = 0;
        private int _turnCount  = 0;

        public DebugLogWindow()
        {
            InitializeComponent();
            LogListBox.ItemsSource = _filteredLogs;
        }

        // ---------------------------------------------------------------
        // 外部から呼び出すログ追加メソッド
        // ---------------------------------------------------------------

        /// <summary>
        /// AutoDebateService.DebugLogEmitted イベントから呼び出す
        /// </summary>
        public void AppendLog(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AppendLog(message));
                return;
            }

            var entry = new DebugLogEntry { Message = message };
            _allLogs.Add(entry);

            // カウンター更新
            _turnCount++;
            if (entry.IsRound) _roundCount++;

            // フッターラベル更新
            TotalLinesLabel.Text = $"ログ: {_allLogs.Count} 行";
            TurnCountLabel.Text  = $"総送信: {_turnCount}";
            if (entry.IsRound)
                RoundCountLabel.Text = $"往復: {_roundCount}";

            // フィルター適用
            string filter = FilterTextBox.Text.Trim();
            if (string.IsNullOrEmpty(filter) || message.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                _filteredLogs.Add(entry);
                if (AutoScrollCheckBox.IsChecked == true && _filteredLogs.Count > 0)
                    LogListBox.ScrollIntoView(_filteredLogs[_filteredLogs.Count - 1]);
            }
        }

        // ---------------------------------------------------------------
        // UI イベント
        // ---------------------------------------------------------------

        private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
            => ApplyFilter();

        private void ApplyFilter()
        {
            string filter = FilterTextBox.Text.Trim();
            _filteredLogs.Clear();
            foreach (var entry in _allLogs)
            {
                if (string.IsNullOrEmpty(filter) ||
                    entry.Message.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    _filteredLogs.Add(entry);
                }
            }
            TotalLinesLabel.Text = $"ログ: {_allLogs.Count} 行 (表示: {_filteredLogs.Count}行)";
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _allLogs.Clear();
            _filteredLogs.Clear();
            _roundCount = 0;
            _turnCount  = 0;
            TotalLinesLabel.Text = "ログ: 0 行";
            RoundCountLabel.Text = "往復: 0";
            TurnCountLabel.Text  = "総送信: 0";
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_allLogs.Count == 0) return;
            var sb = new StringBuilder();
            foreach (var entry in _allLogs)
                sb.AppendLine(entry.Message);
            Clipboard.SetText(sb.ToString());
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // 閉じる代わりに非表示にする（ログを保持したまま）
            e.Cancel = true;
            Hide();
        }
    }
}
