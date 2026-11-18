using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace gironWin
{
    public partial class MainWindow
    {
        // Rebind the existing XAML handlers once, without changing the layout.
        private void BindVisibleLogMenuActions()
        {
            var exports = FindLogicalMenus(this)
                .SelectMany(menu => EnumerateMenuItems(menu))
                .Where(item => item.Header is string header
                    && header == "ログをエクスポート")
                .ToList();
            if (exports.Count != 1)
                throw new InvalidOperationException("ログエクスポートメニューを一意に特定できません。");
            var export = exports[0];
            export.Click -= MenuExportLog_Click;
            export.Click -= MenuExportVisibleRecords_Click;
            export.Click += MenuExportVisibleRecords_Click;
            MenuModeratorSummary.Click -= MenuModeratorSummary_Click;
            MenuModeratorSummary.Click -= MenuVisibleSummary_Click;
            MenuModeratorSummary.Click += MenuVisibleSummary_Click;
        }

        private static IEnumerable<Menu> FindLogicalMenus(DependencyObject parent)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(parent))
            {
                if (child is Menu menu)
                {
                    yield return menu;
                    continue;
                }
                if (child is DependencyObject dependency)
                    foreach (var nested in FindLogicalMenus(dependency))
                        yield return nested;
            }
        }

        private static IEnumerable<MenuItem> EnumerateMenuItems(ItemsControl parent)
        {
            foreach (var item in parent.Items.OfType<MenuItem>())
            {
                yield return item;
                foreach (var nested in EnumerateMenuItems(item))
                    yield return nested;
            }
        }

        private List<TransferRecord> SnapshotVisibleRecords()
            => _turnRecords.Where(record => record != null)
                .OrderBy(record => record.TurnNumber)
                .ThenBy(record => record.Timestamp).ToList();

        private bool CheckVisibleRecords(IReadOnlyList<TransferRecord> records)
        {
            if (records.Count > 0) return true;
            MessageBox.Show(this,
                "ホーム画面のログ一覧に対象のログがありません。",
                "討論ログ", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        private void MenuExportVisibleRecords_Click(object sender, RoutedEventArgs e)
        {
            if (_isWindowClosing || _webViewsReleased) return;
            try
            {
                var records = SnapshotVisibleRecords();
                if (!CheckVisibleRecords(records)) return;
                var dialog = new LogExportOptionsDialog { Owner = this };
                if (dialog.ShowDialog() != true || dialog.Options == null) return;
                if (_isWindowClosing || _webViewsReleased) return;
                var result = new ExportService().ExportAiSiteHtmlTabLogs(records, dialog.Options);
                MessageBox.Show(this, result.Message, "エクスポート", MessageBoxButton.OK,
                    result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                AppLogger.Error(LogCategory.System, "ホーム画面のログエクスポートに失敗しました。", ex);
                MessageBox.Show(this, $"エクスポートに失敗しました。\n{ex.Message}",
                    "エクスポート", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuVisibleSummary_Click(object sender, RoutedEventArgs e)
        {
            if (_isWindowClosing || _webViewsReleased) return;
            try
            {
                var records = SnapshotVisibleRecords();
                if (!CheckVisibleRecords(records)) return;
                new ModeratorSummaryWindow(records) { Owner = this }.Show();
            }
            catch (Exception ex)
            {
                AppLogger.Error(LogCategory.System, "司会サマリーの表示に失敗しました。", ex);
                MessageBox.Show(this, $"司会サマリーを表示できません。\n{ex.Message}",
                    "司会サマリー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
