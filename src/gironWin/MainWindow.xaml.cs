using Microsoft.Web.WebView2.Core;
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
        private TransferService _transferService = null!;
        private AutoDebateService _autoDebateService = null!;
        private readonly ApprovalQueue _approvalQueue = new();

        public ObservableCollection<TransferRecord> TransferRecords => _transferRecords;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _transferService = new TransferService(_adapterResolver, _transferRecords);
            _transferService.DebugLog += (_, msg) =>
            {
                Dispatcher.Invoke(() =>
                {
                    StatusTextBlock.Text = msg;
                    System.Diagnostics.Debug.WriteLine(msg);
                });
            };
            _autoDebateService = new AutoDebateService(_transferService, _approvalQueue, _adapterResolver);
            _autoDebateService.StatusChanged += (_, msg) => Dispatcher.Invoke(() => SetStatus(msg));
            _autoDebateService.TurnAdvanced += (_, turn) => Dispatcher.Invoke(() => TurnCountTextBlock.Text = $"ターン: {turn}");
            _autoDebateService.DebateStopped += (_, _) => Dispatcher.Invoke(() => UpdateDebateButtons(false));

            await InitializeWebViewsAsync();
            SetStatus("準備完了。");
        }

        // ---------------------------------------------------------------
        // WebView2 初期化
        // ---------------------------------------------------------------

        private async Task InitializeWebViewsAsync()
        {
            var env = await CoreWebView2Environment.CreateAsync();

            await LeftWebView.EnsureCoreWebView2Async(env);
            await RightWebView.EnsureCoreWebView2Async(env);

            LeftWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            LeftWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;

            RightWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            RightWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;

            NavigateTo(LeftWebView, LeftUrlTextBox.Text);
            NavigateTo(RightWebView, RightUrlTextBox.Text);
        }

        private void NavigateTo(Microsoft.Web.WebView2.Wpf.WebView2 webView, string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                webView.Source = uri;
        }

        // ---------------------------------------------------------------
        // ステータス
        // ---------------------------------------------------------------

        private void SetStatus(string message)
        {
            StatusTextBlock.Text = message;
        }

        // ---------------------------------------------------------------
        // 転送ヘルパー
        // ---------------------------------------------------------------

        private bool AppendBridge => AppendBridgeCheckBox.IsChecked == true;
        private bool ConfirmBeforeSend => ConfirmBeforeSendCheckBox.IsChecked == true;

        private async Task<string?> ConfirmTextAsync(string text, string title)
        {
            if (!ConfirmBeforeSend)
                return text;

            var win = new TextPreviewWindow(text) { Owner = this, Title = title };
            return win.ShowDialog() == true ? win.EditedText : null;
        }

        private async Task RunTransferAsync(
            Microsoft.Web.WebView2.Wpf.WebView2 sourceWebView,
            Microsoft.Web.WebView2.Wpf.WebView2 targetWebView,
            string sourceUrl,
            string targetUrl,
            bool submit)
        {
            // 送信前確認が必要な場合は選択文を先読みしてプレビューへ
            string? overrideText = null;
            if (ConfirmBeforeSend)
            {
                var sourceAdapter = _adapterResolver.Resolve(sourceUrl);
                if (sourceAdapter != null)
                {
                    string selected = await sourceAdapter.GetSelectedTextAsync(sourceWebView);
                    string built = AppendBridge
                        ? $"{selected}\n\nこのように考えていますがどうですか？"
                        : selected;

                    overrideText = await ConfirmTextAsync(built, "送信前確認");
                    if (overrideText == null)
                    {
                        SetStatus("転送をキャンセルしました。");
                        return;
                    }
                }
            }

            var result = await _transferService.TransferAsync(
                sourceWebView,
                targetWebView,
                sourceUrl,
                targetUrl,
                submit,
                AppendBridge,
                overrideText);

            SetStatus(result.Message);
        }

        private async Task RunReuseAsync(
            TransferRecord? record,
            Microsoft.Web.WebView2.Wpf.WebView2 targetWebView,
            string targetUrl,
            bool submit)
        {
            if (record == null)
            {
                SetStatus("履歴が選択されていません。");
                return;
            }

            string? text = await ConfirmTextAsync(
                record.Text,
                $"履歴再利用 - {record.Direction}");

            if (text == null)
            {
                SetStatus("履歴再利用をキャンセルしました。");
                return;
            }

            var result = await _transferService.ReuseAsync(
                record, targetWebView, targetUrl, submit, text);

            SetStatus(result.Message);
        }

        private TransferRecord? GetSelectedRecord() =>
            TransferHistoryListView.SelectedItem as TransferRecord;

        // ---------------------------------------------------------------
        // ナビゲーション
        // ---------------------------------------------------------------

        private void LeftGoButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateTo(LeftWebView, LeftUrlTextBox.Text);
            SetStatus("左 WebView を移動しました。");
        }

        private void RightGoButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateTo(RightWebView, RightUrlTextBox.Text);
            SetStatus("右 WebView を移動しました。");
        }

        // ---------------------------------------------------------------
        // 転送ボタン
        // ---------------------------------------------------------------

        private async void SendLeftSelectionToRightInputButton_Click(object sender, RoutedEventArgs e) =>
            await RunTransferAsync(LeftWebView, RightWebView, LeftUrlTextBox.Text, RightUrlTextBox.Text, false);

        private async void SendLeftSelectionToRightSubmitButton_Click(object sender, RoutedEventArgs e) =>
            await RunTransferAsync(LeftWebView, RightWebView, LeftUrlTextBox.Text, RightUrlTextBox.Text, true);

        private async void SendRightSelectionToLeftInputButton_Click(object sender, RoutedEventArgs e) =>
            await RunTransferAsync(RightWebView, LeftWebView, RightUrlTextBox.Text, LeftUrlTextBox.Text, false);

        private async void SendRightSelectionToLeftSubmitButton_Click(object sender, RoutedEventArgs e) =>
            await RunTransferAsync(RightWebView, LeftWebView, RightUrlTextBox.Text, LeftUrlTextBox.Text, true);

        // ---------------------------------------------------------------
        // 履歴操作
        // ---------------------------------------------------------------

        private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            _transferRecords.Clear();
            SetStatus("履歴をクリアしました。");
        }

        private void TransferHistoryListView_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
            OpenHistoryPreview(GetSelectedRecord());

        private void TransferHistoryListViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListViewItem { Content: TransferRecord record })
                OpenHistoryPreview(record);
        }

        private void OpenHistoryPreview(TransferRecord? record)
        {
            if (record == null) { SetStatus("履歴が選択されていません。"); return; }

            var win = new TextPreviewWindow(record.Text)
            {
                Owner = this,
                Title = $"履歴詳細 - {record.Direction}"
            };
            win.ShowDialog();
            SetStatus($"履歴詳細: {record.Direction}");
        }

        // ---------------------------------------------------------------
        // 右クリックメニュー
        // ---------------------------------------------------------------

        private void CopySelectedHistoryTextMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var record = GetSelectedRecord();
            if (record == null) { SetStatus("コピー対象の履歴が選択されていません。"); return; }
            Clipboard.SetText(record.Text ?? string.Empty);
            SetStatus($"履歴をコピーしました: {record.Direction}");
        }

        private void OpenSelectedHistoryPreviewMenuItem_Click(object sender, RoutedEventArgs e) =>
            OpenHistoryPreview(GetSelectedRecord());

        private async void ReuseToLeftInputMenuItem_Click(object sender, RoutedEventArgs e) =>
            await RunReuseAsync(GetSelectedRecord(), LeftWebView, LeftUrlTextBox.Text, false);

        private async void ReuseToLeftSubmitMenuItem_Click(object sender, RoutedEventArgs e) =>
            await RunReuseAsync(GetSelectedRecord(), LeftWebView, LeftUrlTextBox.Text, true);

        private async void ReuseToRightInputMenuItem_Click(object sender, RoutedEventArgs e) =>
            await RunReuseAsync(GetSelectedRecord(), RightWebView, RightUrlTextBox.Text, false);

        private async void ReuseToRightSubmitMenuItem_Click(object sender, RoutedEventArgs e) =>
            await RunReuseAsync(GetSelectedRecord(), RightWebView, RightUrlTextBox.Text, true);

        // ---------------------------------------------------------------
        // 自動討論
        // ---------------------------------------------------------------

        private void StartAutoDebateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_autoDebateService.IsRunning) return;

            _autoDebateService.Start(new AutoDebateConfig
            {
                LeftWebView = LeftWebView,
                RightWebView = RightWebView,
                LeftUrl = LeftUrlTextBox.Text,
                RightUrl = RightUrlTextBox.Text,
                AppendBridge = AppendBridgeCheckBox.IsChecked == true,
                RequireApproval = ConfirmBeforeSendCheckBox.IsChecked == true,
                MaxTurns = 0,
                TurnIntervalMs = 2000,
                GenerationTimeoutMs = 90000
            });

            UpdateDebateButtons(true);
        }

        private void StopAutoDebateButton_Click(object sender, RoutedEventArgs e)
        {
            _autoDebateService.Stop();
            UpdateDebateButtons(false);
        }

        private void PauseResumeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_autoDebateService.IsPaused)
            {
                _autoDebateService.Resume();
                PauseResumeButton.Content = "一時停止";
            }
            else
            {
                _autoDebateService.Pause();
                PauseResumeButton.Content = "再開";
            }
        }

        private void UpdateDebateButtons(bool running)
        {
            StartAutoDebateButton.IsEnabled = !running;
            StopAutoDebateButton.IsEnabled = running;
            PauseResumeButton.IsEnabled = running;
            if (!running) PauseResumeButton.Content = "一時停止";
        }
    }
}