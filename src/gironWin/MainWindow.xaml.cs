using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace gironWin
{
    public partial class MainWindow : Window
    {
        private readonly AiSiteAdapterResolver _adapterResolver = new();
        private readonly ObservableCollection<TransferRecord> _transferRecords = new();

        public ObservableCollection<TransferRecord> TransferRecords => _transferRecords;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializeWebViewsAsync();
            SetStatus("WebView2 を初期化しました。");
        }

        private async Task InitializeWebViewsAsync()
        {
            var env = await CoreWebView2Environment.CreateAsync();

            await LeftWebView.EnsureCoreWebView2Async(env);
            await RightWebView.EnsureCoreWebView2Async(env);

            LeftWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            LeftWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;

            RightWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            RightWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;

            if (Uri.TryCreate(LeftUrlTextBox.Text, UriKind.Absolute, out var leftUri))
            {
                LeftWebView.Source = leftUri;
            }

            if (Uri.TryCreate(RightUrlTextBox.Text, UriKind.Absolute, out var rightUri))
            {
                RightWebView.Source = rightUri;
            }
        }

        private void SetStatus(string message)
        {
            StatusTextBlock.Text = message;
        }

        private string BuildTransferText(string sourceText)
        {
            if (string.IsNullOrWhiteSpace(sourceText))
            {
                return string.Empty;
            }

            if (AppendBridgeCheckBox.IsChecked == true)
            {
                return $"{sourceText}\n\nこのように考えていますがどうですか？";
            }

            return sourceText;
        }

        private void AddTransferRecord(
            string sourceSite,
            string targetSite,
            string text,
            bool submitted,
            string status)
        {
            _transferRecords.Insert(0, new TransferRecord
            {
                Timestamp = DateTime.Now,
                SourceSite = sourceSite,
                TargetSite = targetSite,
                Direction = $"{sourceSite} → {targetSite}",
                Text = text,
                Submitted = submitted,
                Status = status
            });
        }

        private async Task<bool> TransferAsync(
            WebView2 sourceWebView,
            WebView2 targetWebView,
            string sourceUrl,
            string targetUrl,
            bool submit)
        {
            var sourceAdapter = _adapterResolver.Resolve(sourceUrl);
            var targetAdapter = _adapterResolver.Resolve(targetUrl);

            if (sourceAdapter == null)
            {
                SetStatus("送信元サイトのアダプタが見つかりません。");
                return false;
            }

            if (targetAdapter == null)
            {
                SetStatus("送信先サイトのアダプタが見つかりません。");
                return false;
            }

            SetStatus($"{sourceAdapter.SiteName} → {targetAdapter.SiteName} の転送を開始しました。");

            string selectedText = await sourceAdapter.GetSelectedTextAsync(sourceWebView);
            string text = BuildTransferText(selectedText);

            if (string.IsNullOrWhiteSpace(text))
            {
                SetStatus("選択された文字列がありません。");
                return false;
            }

            string finalText = text;

            if (ConfirmBeforeSendCheckBox.IsChecked == true)
            {
                var previewWindow = new TextPreviewWindow(text)
                {
                    Owner = this
                };

                bool? previewResult = previewWindow.ShowDialog();
                if (previewResult != true)
                {
                    AddTransferRecord(sourceAdapter.SiteName, targetAdapter.SiteName, text, submit, "キャンセル");
                    SetStatus("転送をキャンセルしました。");
                    return false;
                }

                finalText = previewWindow.EditedText;
            }

            if (string.IsNullOrWhiteSpace(finalText))
            {
                AddTransferRecord(sourceAdapter.SiteName, targetAdapter.SiteName, finalText, submit, "空文字");
                SetStatus("送信テキストが空です。");
                return false;
            }

            bool inputOk = await targetAdapter.SetInputAsync(targetWebView, finalText);
            if (!inputOk)
            {
                AddTransferRecord(sourceAdapter.SiteName, targetAdapter.SiteName, finalText, submit, "入力失敗");
                SetStatus($"{targetAdapter.SiteName} の入力欄が見つかりませんでした。");
                return false;
            }

            if (!submit)
            {
                AddTransferRecord(sourceAdapter.SiteName, targetAdapter.SiteName, finalText, false, "入力のみ");
                SetStatus($"{targetAdapter.SiteName} へ入力しました。");
                return true;
            }

            await Task.Delay(300);

            bool sendOk = await targetAdapter.SendAsync(targetWebView);
            if (!sendOk)
            {
                AddTransferRecord(sourceAdapter.SiteName, targetAdapter.SiteName, finalText, true, "送信失敗");
                SetStatus($"{targetAdapter.SiteName} への送信に失敗しました。");
                return false;
            }

            AddTransferRecord(sourceAdapter.SiteName, targetAdapter.SiteName, finalText, true, "送信成功");
            SetStatus($"{targetAdapter.SiteName} へ送信しました。");
            return true;
        }

        private TransferRecord? GetSelectedTransferRecord()
        {
            return TransferHistoryListView.SelectedItem as TransferRecord;
        }

        private async Task<bool> ReuseRecordAsync(
            TransferRecord record,
            WebView2 targetWebView,
            string targetUrl,
            bool submit)
        {
            if (record == null)
            {
                SetStatus("履歴が選択されていません。");
                return false;
            }

            var targetAdapter = _adapterResolver.Resolve(targetUrl);
            if (targetAdapter == null)
            {
                SetStatus("送信先サイトのアダプタが見つかりません。");
                return false;
            }

            string finalText = record.Text;

            if (ConfirmBeforeSendCheckBox.IsChecked == true)
            {
                var previewWindow = new TextPreviewWindow(record.Text)
                {
                    Owner = this,
                    Title = $"履歴再利用 - {targetAdapter.SiteName}"
                };

                bool? previewResult = previewWindow.ShowDialog();
                if (previewResult != true)
                {
                    AddTransferRecord(record.SourceSite, targetAdapter.SiteName, record.Text, submit, "履歴再利用キャンセル");
                    SetStatus("履歴再利用をキャンセルしました。");
                    return false;
                }

                finalText = previewWindow.EditedText;
            }

            if (string.IsNullOrWhiteSpace(finalText))
            {
                AddTransferRecord(record.SourceSite, targetAdapter.SiteName, finalText, submit, "履歴再利用空文字");
                SetStatus("履歴再利用テキストが空です。");
                return false;
            }

            bool inputOk = await targetAdapter.SetInputAsync(targetWebView, finalText);
            if (!inputOk)
            {
                AddTransferRecord(record.SourceSite, targetAdapter.SiteName, finalText, submit, "履歴再利用入力失敗");
                SetStatus($"{targetAdapter.SiteName} の入力欄が見つかりませんでした。");
                return false;
            }

            if (!submit)
            {
                AddTransferRecord(record.SourceSite, targetAdapter.SiteName, finalText, false, "履歴再利用入力");
                SetStatus($"履歴を {targetAdapter.SiteName} へ入力しました。");
                return true;
            }

            await Task.Delay(300);

            bool sendOk = await targetAdapter.SendAsync(targetWebView);
            if (!sendOk)
            {
                AddTransferRecord(record.SourceSite, targetAdapter.SiteName, finalText, true, "履歴再利用送信失敗");
                SetStatus($"{targetAdapter.SiteName} への履歴再送信に失敗しました。");
                return false;
            }

            AddTransferRecord(record.SourceSite, targetAdapter.SiteName, finalText, true, "履歴再利用送信成功");
            SetStatus($"履歴を {targetAdapter.SiteName} へ送信しました。");
            return true;
        }

        private void OpenHistoryPreview(TransferRecord? record)
        {
            if (record == null)
            {
                SetStatus("履歴が選択されていません。");
                return;
            }

            var previewWindow = new TextPreviewWindow(record.Text)
            {
                Owner = this,
                Title = $"履歴詳細 - {record.Direction}"
            };

            previewWindow.ShowDialog();
            SetStatus($"履歴詳細を表示しました: {record.Direction}");
        }

        private void CopySelectedHistoryText()
        {
            var record = GetSelectedTransferRecord();
            if (record == null)
            {
                SetStatus("コピー対象の履歴が選択されていません。");
                return;
            }

            Clipboard.SetText(record.Text ?? string.Empty);
            SetStatus($"履歴をコピーしました: {record.Direction}");
        }

        private void LeftGoButton_Click(object sender, RoutedEventArgs e)
        {
            if (Uri.TryCreate(LeftUrlTextBox.Text, UriKind.Absolute, out var uri))
            {
                LeftWebView.Source = uri;
                SetStatus("左WebViewを移動しました。");
            }
            else
            {
                SetStatus("左URLが不正です。");
            }
        }

        private void RightGoButton_Click(object sender, RoutedEventArgs e)
        {
            if (Uri.TryCreate(RightUrlTextBox.Text, UriKind.Absolute, out var uri))
            {
                RightWebView.Source = uri;
                SetStatus("右WebViewを移動しました。");
            }
            else
            {
                SetStatus("右URLが不正です。");
            }
        }

        private async void SendLeftSelectionToRightInputButton_Click(object sender, RoutedEventArgs e)
        {
            bool ok = await TransferAsync(
                LeftWebView,
                RightWebView,
                LeftUrlTextBox.Text,
                RightUrlTextBox.Text,
                false);

            SetStatus(ok ? "左から右へ入力しました。" : "左から右への入力に失敗しました。");
        }

        private async void SendLeftSelectionToRightSubmitButton_Click(object sender, RoutedEventArgs e)
        {
            bool ok = await TransferAsync(
                LeftWebView,
                RightWebView,
                LeftUrlTextBox.Text,
                RightUrlTextBox.Text,
                true);

            SetStatus(ok ? "左から右へ送信しました。" : "左から右への送信に失敗しました。");
        }

        private async void SendRightSelectionToLeftInputButton_Click(object sender, RoutedEventArgs e)
        {
            bool ok = await TransferAsync(
                RightWebView,
                LeftWebView,
                RightUrlTextBox.Text,
                LeftUrlTextBox.Text,
                false);

            SetStatus(ok ? "右から左へ入力しました。" : "右から左への入力に失敗しました。");
        }

        private async void SendRightSelectionToLeftSubmitButton_Click(object sender, RoutedEventArgs e)
        {
            bool ok = await TransferAsync(
                RightWebView,
                LeftWebView,
                RightUrlTextBox.Text,
                LeftUrlTextBox.Text,
                true);

            SetStatus(ok ? "右から左へ送信しました。" : "右から左への送信に失敗しました。");
        }

        private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            _transferRecords.Clear();
            SetStatus("履歴をクリアしました。");
        }

        private void TransferHistoryListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OpenHistoryPreview(GetSelectedTransferRecord());
        }

        private void TransferHistoryListViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListViewItem item && item.Content is TransferRecord record)
            {
                OpenHistoryPreview(record);
            }
        }

        private void CopySelectedHistoryTextMenuItem_Click(object sender, RoutedEventArgs e)
        {
            CopySelectedHistoryText();
        }

        private void OpenSelectedHistoryPreviewMenuItem_Click(object sender, RoutedEventArgs e)
        {
            OpenHistoryPreview(GetSelectedTransferRecord());
        }

        private async void ReuseToLeftInputMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var record = GetSelectedTransferRecord();
            bool ok = await ReuseRecordAsync(record, LeftWebView, LeftUrlTextBox.Text, false);
            SetStatus(ok ? "履歴を左へ入力しました。" : "履歴の左入力に失敗しました。");
        }

        private async void ReuseToLeftSubmitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var record = GetSelectedTransferRecord();
            bool ok = await ReuseRecordAsync(record, LeftWebView, LeftUrlTextBox.Text, true);
            SetStatus(ok ? "履歴を左へ送信しました。" : "履歴の左送信に失敗しました。");
        }

        private async void ReuseToRightInputMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var record = GetSelectedTransferRecord();
            bool ok = await ReuseRecordAsync(record, RightWebView, RightUrlTextBox.Text, false);
            SetStatus(ok ? "履歴を右へ入力しました。" : "履歴の右入力に失敗しました。");
        }

        private async void ReuseToRightSubmitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var record = GetSelectedTransferRecord();
            bool ok = await ReuseRecordAsync(record, RightWebView, RightUrlTextBox.Text, true);
            SetStatus(ok ? "履歴を右へ送信しました。" : "履歴の右送信に失敗しました。");
        }
    }
}