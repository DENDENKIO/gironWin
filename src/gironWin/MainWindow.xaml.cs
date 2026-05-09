using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace gironWin
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<TransferRecord> _transferRecords = new();
        public ObservableCollection<TransferRecord> TransferRecords => _transferRecords;
        private readonly AiSiteAdapterResolver _adapterResolver = new();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializeWebViewsAsync();
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

            LeftWebView.Source = new Uri(LeftUrlTextBox.Text);
            RightWebView.Source = new Uri(RightUrlTextBox.Text);
        }

        private void LeftGoButton_Click(object sender, RoutedEventArgs e)
        {
            LeftWebView.Source = new Uri(LeftUrlTextBox.Text);
            SetStatus($"左側を移動: {LeftUrlTextBox.Text}");
        }

        private void RightGoButton_Click(object sender, RoutedEventArgs e)
        {
            RightWebView.Source = new Uri(RightUrlTextBox.Text);
            SetStatus($"右側を移動: {RightUrlTextBox.Text}");
        }

        private string BuildTransferText(string sourceText)
        {
            if (string.IsNullOrWhiteSpace(sourceText)) return string.Empty;
            if (AppendBridgeCheckBox.IsChecked == true)
            {
                return $"{sourceText}\n\nこのように考えていますがどうですか？";
            }
            return sourceText;
        }

        private void SetStatus(string message)
        {
            StatusTextBlock.Text = message;
        }

        private async Task<bool> TransferAsync(
            Microsoft.Web.WebView2.Wpf.WebView2 sourceWebView,
            Microsoft.Web.WebView2.Wpf.WebView2 targetWebView,
            string sourceUrl,
            string targetUrl,
            bool submit)
        {
            var sourceAdapter = _adapterResolver.Resolve(sourceUrl);
            var targetAdapter = _adapterResolver.Resolve(targetUrl);

            if (sourceAdapter == null || targetAdapter == null)
            {
                SetStatus("アダプタが見つかりません。");
                return false;
            }

            SetStatus($"{sourceAdapter.SiteName} → {targetAdapter.SiteName} の転送を開始");

            string selectedText = await sourceAdapter.GetSelectedTextAsync(sourceWebView);
            string text = BuildTransferText(selectedText);

            if (string.IsNullOrWhiteSpace(text))
            {
                SetStatus("転送失敗: 選択テキストなし");
                return false;
            }

            string finalText = text;

            if (ConfirmBeforeSendCheckBox.IsChecked == true)
            {
                var previewWindow = new TextPreviewWindow(text) { Owner = this };
                if (previewWindow.ShowDialog() != true)
                {
                    SetStatus("転送をキャンセルしました");
                    return false;
                }
                finalText = previewWindow.EditedText;
            }

            if (string.IsNullOrWhiteSpace(finalText))
            {
                SetStatus("転送失敗: 送信テキストが空");
                return false;
            }

            bool inputOk = await targetAdapter.SetInputAsync(targetWebView, finalText);
            if (!inputOk)
            {
                SetStatus($"{targetAdapter.SiteName} への入力に失敗しました");
                return false;
            }

            if (!submit)
            {
                AddTransferRecord(sourceAdapter.SiteName, targetAdapter.SiteName, finalText, false, "入力のみ");
                SetStatus($"{targetAdapter.SiteName} へ入力しました");
                return true;
            }

            await Task.Delay(300);

            bool sendOk = await targetAdapter.SendAsync(targetWebView);
            if (!sendOk)
            {
                SetStatus($"{targetAdapter.SiteName} への送信に失敗しました");
                return false;
            }

            AddTransferRecord(sourceAdapter.SiteName, targetAdapter.SiteName, finalText, true, "送信成功");
            SetStatus($"{targetAdapter.SiteName} へ送信しました");
            return true;
        }

        private void AddTransferRecord(string sourceSite, string targetSite, string text, bool submitted, string status)
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

        private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            _transferRecords.Clear();
            SetStatus("履歴をクリアしました。");
        }

        private async void SaveHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog { Filter = "JSON files (*.json)|*.json", FileName = $"history-{DateTime.Now:yyyyMMdd}.json" };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    string json = JsonSerializer.Serialize(_transferRecords.ToList(), new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(dialog.FileName, json);
                    SetStatus("履歴を保存しました。");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"保存エラー: {ex.Message}");
                }
            }
        }

        private async void LoadHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "JSON files (*.json)|*.json" };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    string json = await File.ReadAllTextAsync(dialog.FileName);
                    var records = JsonSerializer.Deserialize<List<TransferRecord>>(json);
                    _transferRecords.Clear();
                    if (records != null)
                    {
                        foreach (var r in records.OrderByDescending(x => x.Timestamp)) _transferRecords.Add(r);
                    }
                    SetStatus("履歴を読み込みました。");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"読込エラー: {ex.Message}");
                }
            }
        }

        private TransferRecord? GetSelectedTransferRecord() => TransferHistoryListView.SelectedItem as TransferRecord;

        private async Task<bool> ReuseRecordAsync(TransferRecord record, Microsoft.Web.WebView2.Wpf.WebView2 targetWebView, string targetUrl, bool submit)
        {
            var targetAdapter = _adapterResolver.Resolve(targetUrl);
            if (targetAdapter == null) return false;

            string finalText = record.Text;
            if (ConfirmBeforeSendCheckBox.IsChecked == true)
            {
                var previewWindow = new TextPreviewWindow(record.Text) { Owner = this };
                if (previewWindow.ShowDialog() != true) return false;
                finalText = previewWindow.EditedText;
            }

            bool ok = await targetAdapter.SetInputAsync(targetWebView, finalText);
            if (ok && submit)
            {
                await Task.Delay(300);
                ok = await targetAdapter.SendAsync(targetWebView);
            }
            if (ok) AddTransferRecord(record.SourceSite, targetAdapter.SiteName, finalText, submit, "再利用成功");
            return ok;
        }

        private void CopySelectedHistoryText()
        {
            var record = GetSelectedTransferRecord();
            if (record == null) { SetStatus("履歴未選択"); return; }
            Clipboard.SetText(record.Text ?? string.Empty);
            SetStatus("履歴をコピーしました。");
        }

        private void OpenHistoryPreview(TransferRecord? record)
        {
            if (record == null) { SetStatus("履歴未選択"); return; }
            new TextPreviewWindow(record.Text) { Owner = this }.ShowDialog();
        }

        private void TransferHistoryListView_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenHistoryPreview(GetSelectedTransferRecord());

        private void CopySelectedHistoryTextMenuItem_Click(object sender, RoutedEventArgs e) => CopySelectedHistoryText();

        private void OpenSelectedHistoryPreviewMenuItem_Click(object sender, RoutedEventArgs e) => OpenHistoryPreview(GetSelectedTransferRecord());

        private async void ReuseToLeftInputMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var record = GetSelectedTransferRecord();
            if (record == null) return;
            bool ok = await ReuseRecordAsync(record, LeftWebView, LeftUrlTextBox.Text, false);
            SetStatus(ok ? "左入力に再利用しました。" : "左入力に失敗しました。");
        }

        private async void ReuseToLeftSubmitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var record = GetSelectedTransferRecord();
            if (record == null) return;
            bool ok = await ReuseRecordAsync(record, LeftWebView, LeftUrlTextBox.Text, true);
            SetStatus(ok ? "左送信に再利用しました。" : "左送信に失敗しました。");
        }

        private async void ReuseToRightInputMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var record = GetSelectedTransferRecord();
            if (record == null) return;
            bool ok = await ReuseRecordAsync(record, RightWebView, RightUrlTextBox.Text, false);
            SetStatus(ok ? "右入力に再利用しました。" : "右入力に失敗しました。");
        }

        private async void ReuseToRightSubmitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var record = GetSelectedTransferRecord();
            if (record == null) return;
            bool ok = await ReuseRecordAsync(record, RightWebView, RightUrlTextBox.Text, true);
            SetStatus(ok ? "右送信に再利用しました。" : "右送信に失敗しました。");
        }

        private async void SendLeftSelectionToRightInputButton_Click(object sender, RoutedEventArgs e)
        {
            bool ok = await TransferAsync(LeftWebView, RightWebView, LeftUrlTextBox.Text, RightUrlTextBox.Text, false);
            SetStatus(ok ? "左から右へ入力しました。" : "左から右への入力に失敗しました。");
        }

        private async void SendLeftSelectionToRightSubmitButton_Click(object sender, RoutedEventArgs e)
        {
            bool ok = await TransferAsync(LeftWebView, RightWebView, LeftUrlTextBox.Text, RightUrlTextBox.Text, true);
            SetStatus(ok ? "左から右へ送信しました。" : "左から右への送信に失敗しました。");
        }

        private async void SendRightSelectionToLeftInputButton_Click(object sender, RoutedEventArgs e)
        {
            bool ok = await TransferAsync(RightWebView, LeftWebView, RightUrlTextBox.Text, LeftUrlTextBox.Text, false);
            SetStatus(ok ? "右から左へ入力しました。" : "右から左への入力に失敗しました。");
        }

        private async void SendRightSelectionToLeftSubmitButton_Click(object sender, RoutedEventArgs e)
        {
            bool ok = await TransferAsync(RightWebView, LeftWebView, RightUrlTextBox.Text, LeftUrlTextBox.Text, true);
            SetStatus(ok ? "右から左へ送信しました。" : "右から左への送信に失敗しました。");
        }

        private async Task<string> ExecuteScriptStringAsync(Microsoft.Web.WebView2.Wpf.WebView2 webView, string script)
        {
            if (webView?.CoreWebView2 == null) return string.Empty;
            string json = await webView.ExecuteScriptAsync(script);
            if (string.IsNullOrWhiteSpace(json) || json == "null") return string.Empty;
            try { return JsonSerializer.Deserialize<string>(json) ?? string.Empty; }
            catch { return json.Trim('"'); }
        }
    }
}