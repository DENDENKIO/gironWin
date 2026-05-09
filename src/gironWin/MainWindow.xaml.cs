using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
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
        private TransferService    _transferService   = null!;
        private AutoDebateService  _autoDebateService = null!;
        private readonly ApprovalQueue _approvalQueue = new();
        private SessionRepository  _sessionRepo       = null!;
        private readonly LoopDetector _loopLeft  = new();
        private readonly LoopDetector _loopRight = new();

        // FR-06 役割プロンプト
        private string _leftSystemPrompt  = string.Empty;
        private string _rightSystemPrompt = string.Empty;

        public ObservableCollection<TransferRecord> TransferRecords => _transferRecords;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _sessionRepo     = new SessionRepository();
            _transferService = new TransferService(_adapterResolver, _transferRecords);
            _transferService.DebugLog += (_, msg) => Dispatcher.Invoke(() =>
            {
                StatusTextBlock.Text = msg;
                System.Diagnostics.Debug.WriteLine(msg);
            });

            _autoDebateService = new AutoDebateService(_transferService, _approvalQueue, _adapterResolver);
            _autoDebateService.StatusChanged += (_, msg)  => Dispatcher.Invoke(() => SetStatus(msg));
            _autoDebateService.TurnAdvanced  += (_, turn) => Dispatcher.Invoke(() => TurnCountTextBlock.Text = $"ターン: {turn}");
            _autoDebateService.DebateStopped += (_, _)    => Dispatcher.Invoke(() => UpdateDebateButtons(false));

            // ターン完了: セッション保存 + ループ検知
            _autoDebateService.TurnAdvanced += async (_, turn) =>
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    if (_transferRecords.Count == 0) return;
                    var last = _transferRecords[_transferRecords.Count - 1];
                    await _sessionRepo.AppendAsync(turn, last.Direction ?? "", last.Text ?? "");

                    bool isLeft = last.Direction?.Contains("左") == true;
                    var detector = isLeft ? _loopLeft : _loopRight;
                    if (detector.AddAndCheck(last.Text ?? ""))
                    {
                        _autoDebateService.Stop();
                        UpdateDebateButtons(false);
                        MessageBox.Show(
                            $"ループを検知したため自動討論を停止しました。\n(ターン {turn})",
                            "ループ検知", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                });
            };

            // FR-09: 承認キュー変化→バナー更新
            _approvalQueue.Items.CollectionChanged += (_, _) => Dispatcher.Invoke(UpdateApprovalBanner);

            foreach (var adapter in _adapterResolver.Adapters)
            {
                if (adapter is GeminiAdapter gemini)
                    gemini.DebugLog += (_, msg) => Dispatcher.Invoke(() =>
                    {
                        StatusTextBlock.Text = msg;
                        System.Diagnostics.Debug.WriteLine(msg);
                    });
            }

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
            NavigateTo(LeftWebView,  LeftUrlTextBox.Text);
            NavigateTo(RightWebView, RightUrlTextBox.Text);
        }

        private void NavigateTo(Microsoft.Web.WebView2.Wpf.WebView2 webView, string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                webView.Source = uri;
        }

        private void SetStatus(string message) => StatusTextBlock.Text = message;

        // ---------------------------------------------------------------
        // 承認バナー (FR-09)
        // ---------------------------------------------------------------
        private void UpdateApprovalBanner()
        {
            int count = _approvalQueue.Items.Count;
            if (count > 0)
            {
                ApprovalBanner.Visibility = Visibility.Visible;
                ApprovalBannerText.Text   = $"承認待ちの送信が {count} 件あります。";
            }
            else
            {
                ApprovalBanner.Visibility = Visibility.Collapsed;
            }
        }

        private void OpenApprovalWindowButton_Click(object sender, RoutedEventArgs e)
        {
            if (_approvalQueue.Items.Count == 0) return;
            var item = _approvalQueue.Items[0];
            var win  = new ApprovalWindow(item) { Owner = this };
            if (win.ShowDialog() == true)
            {
                if (win.IsApproved)
                    _approvalQueue.Approve(item, win.EditedText);
                else
                    _approvalQueue.Reject(item);
            }
        }

        // ---------------------------------------------------------------
        // 転送ヘルパー
        // ---------------------------------------------------------------
        private bool AppendBridge    => AppendBridgeCheckBox.IsChecked == true;
        private bool ConfirmBeforeSend => ConfirmBeforeSendCheckBox.IsChecked == true;

        private async Task<string?> ConfirmTextAsync(string text, string title)
        {
            if (!ConfirmBeforeSend) return text;
            var win = new TextPreviewWindow(text) { Owner = this, Title = title };
            return win.ShowDialog() == true ? win.EditedText : null;
        }

        private async Task RunTransferAsync(
            Microsoft.Web.WebView2.Wpf.WebView2 sourceWebView,
            Microsoft.Web.WebView2.Wpf.WebView2 targetWebView,
            string sourceUrl, string targetUrl, bool submit)
        {
            string? overrideText = null;
            if (ConfirmBeforeSend)
            {
                var srcAdapter = _adapterResolver.Resolve(sourceUrl);
                if (srcAdapter != null)
                {
                    string selected = await srcAdapter.GetSelectedTextAsync(sourceWebView);
                    string built = AppendBridge
                        ? $"{selected}\n\nこのように考えていますがどうですか？"
                        : selected;
                    overrideText = await ConfirmTextAsync(built, "送信前確認");
                    if (overrideText == null) { SetStatus("転送をキャンセルしました。"); return; }
                }
            }
            var result = await _transferService.TransferAsync(
                sourceWebView, targetWebView, sourceUrl, targetUrl,
                submit, AppendBridge, overrideText);
            SetStatus(result.Message);
        }

        private async Task RunReuseAsync(
            TransferRecord? record,
            Microsoft.Web.WebView2.Wpf.WebView2 targetWebView,
            string targetUrl, bool submit)
        {
            if (record == null) { SetStatus("履歴が選択されていません。"); return; }
            string? text = await ConfirmTextAsync(record.Text, $"履歴再利用 - {record.Direction}");
            if (text == null) { SetStatus("履歴再利用をキャンセルしました。"); return; }
            var result = await _transferService.ReuseAsync(record, targetWebView, targetUrl, submit, text);
            SetStatus(result.Message);
        }

        private TransferRecord? GetSelectedRecord() =>
            TransferHistoryListView.SelectedItem as TransferRecord;

        // ---------------------------------------------------------------
        // ナビゲーション
        // ---------------------------------------------------------------
        private void LeftGoButton_Click(object sender, RoutedEventArgs e)
            { NavigateTo(LeftWebView, LeftUrlTextBox.Text); SetStatus("左 WebView を移動しました。"); }

        private void RightGoButton_Click(object sender, RoutedEventArgs e)
            { NavigateTo(RightWebView, RightUrlTextBox.Text); SetStatus("右 WebView を移動しました。"); }

        // ---------------------------------------------------------------
        // 転送ボタン
        // ---------------------------------------------------------------
        private async void SendLeftSelectionToRightInputButton_Click(object s, RoutedEventArgs e)
            => await RunTransferAsync(LeftWebView, RightWebView, LeftUrlTextBox.Text, RightUrlTextBox.Text, false);
        private async void SendLeftSelectionToRightSubmitButton_Click(object s, RoutedEventArgs e)
            => await RunTransferAsync(LeftWebView, RightWebView, LeftUrlTextBox.Text, RightUrlTextBox.Text, true);
        private async void SendRightSelectionToLeftInputButton_Click(object s, RoutedEventArgs e)
            => await RunTransferAsync(RightWebView, LeftWebView, RightUrlTextBox.Text, LeftUrlTextBox.Text, false);
        private async void SendRightSelectionToLeftSubmitButton_Click(object s, RoutedEventArgs e)
            => await RunTransferAsync(RightWebView, LeftWebView, RightUrlTextBox.Text, LeftUrlTextBox.Text, true);

        // ---------------------------------------------------------------
        // 履歴操作
        // ---------------------------------------------------------------
        private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
            { _transferRecords.Clear(); SetStatus("履歴をクリアしました。"); }

        private void TransferHistoryListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
            => OpenHistoryPreview(GetSelectedRecord());

        private void TransferHistoryListViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListViewItem { Content: TransferRecord record }) OpenHistoryPreview(record);
        }

        private void OpenHistoryPreview(TransferRecord? record)
        {
            if (record == null) { SetStatus("履歴が選択されていません。"); return; }
            var win = new TextPreviewWindow(record.Text) { Owner = this, Title = $"履歴詳細 - {record.Direction}" };
            win.ShowDialog();
            SetStatus($"履歴詳細: {record.Direction}");
        }

        // ---------------------------------------------------------------
        // 右クリックメニュー
        // ---------------------------------------------------------------
        private void CopySelectedHistoryTextMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var r = GetSelectedRecord();
            if (r == null) { SetStatus("コピー対象が選択されていません。"); return; }
            Clipboard.SetText(r.Text ?? string.Empty);
            SetStatus($"履歴をコピーしました: {r.Direction}");
        }

        private void OpenSelectedHistoryPreviewMenuItem_Click(object sender, RoutedEventArgs e)
            => OpenHistoryPreview(GetSelectedRecord());

        private async void ReuseToLeftInputMenuItem_Click(object s, RoutedEventArgs e)
            => await RunReuseAsync(GetSelectedRecord(), LeftWebView, LeftUrlTextBox.Text, false);
        private async void ReuseToLeftSubmitMenuItem_Click(object s, RoutedEventArgs e)
            => await RunReuseAsync(GetSelectedRecord(), LeftWebView, LeftUrlTextBox.Text, true);
        private async void ReuseToRightInputMenuItem_Click(object s, RoutedEventArgs e)
            => await RunReuseAsync(GetSelectedRecord(), RightWebView, RightUrlTextBox.Text, false);
        private async void ReuseToRightSubmitMenuItem_Click(object s, RoutedEventArgs e)
            => await RunReuseAsync(GetSelectedRecord(), RightWebView, RightUrlTextBox.Text, true);

        // FR-10 引用返信
        private async void QuoteToLeftMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var r = GetSelectedRecord();
            if (r == null) { SetStatus("引用対象が選択されていません。"); return; }
            string quoted = QuoteService.BuildFullQuote(r);
            var win = new TextPreviewWindow(quoted) { Owner = this, Title = "引用確認 (左へ送信)" };
            if (win.ShowDialog() != true) return;
            var result = await _transferService.ReuseAsync(r, LeftWebView, LeftUrlTextBox.Text, true, win.EditedText);
            SetStatus(result.Message);
        }

        private async void QuoteToRightMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var r = GetSelectedRecord();
            if (r == null) { SetStatus("引用対象が選択されていません。"); return; }
            string quoted = QuoteService.BuildFullQuote(r);
            var win = new TextPreviewWindow(quoted) { Owner = this, Title = "引用確認 (右へ送信)" };
            if (win.ShowDialog() != true) return;
            var result = await _transferService.ReuseAsync(r, RightWebView, RightUrlTextBox.Text, true, win.EditedText);
            SetStatus(result.Message);
        }

        // ---------------------------------------------------------------
        // 自動討論
        // ---------------------------------------------------------------
        private void StartAutoDebateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_autoDebateService.IsRunning) return;
            _loopLeft.Reset();
            _loopRight.Reset();
            _sessionRepo.StartNewSession();

            int maxTurns = 0;
            if (!string.IsNullOrWhiteSpace(MaxTurnsTextBox.Text))
                int.TryParse(MaxTurnsTextBox.Text.Trim(), out maxTurns);
            if (maxTurns < 0) maxTurns = 0;

            _autoDebateService.Start(new AutoDebateConfig
            {
                LeftWebView  = LeftWebView,
                RightWebView = RightWebView,
                LeftUrl      = LeftUrlTextBox.Text,
                RightUrl     = RightUrlTextBox.Text,
                AppendBridge    = AppendBridgeCheckBox.IsChecked == true,
                RequireApproval = ConfirmBeforeSendCheckBox.IsChecked == true,
                MaxTurns        = maxTurns,
                TurnIntervalMs  = 500,
                GenerationTimeoutMs = 90000,
                LeftSystemPrompt  = _leftSystemPrompt,   // FR-06
                RightSystemPrompt = _rightSystemPrompt   // FR-06
            });

            string limitMsg = maxTurns > 0 ? $"（最大{maxTurns}ターン）" : "（無制限）";
            SetStatus($"自動討論を開始しました {limitMsg}");
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

        // FR-08 途中介入
        private async void InterventionButton_Click(object sender, RoutedEventArgs e)
        {
            // 先に一時停止
            bool wasPaused = _autoDebateService.IsPaused;
            if (!wasPaused) _autoDebateService.Pause();

            var win = new InterventionWindow { Owner = this };
            if (win.ShowDialog() != true)
            {
                // キャンセル: 停止していなかったなら再開
                if (!wasPaused) _autoDebateService.Resume();
                return;
            }

            if (win.ShouldSend && !string.IsNullOrWhiteSpace(win.Text))
            {
                // 途中介入テキストを送信
                string text = win.Text;
                if (win.Target == InterventionTarget.Left || win.Target == InterventionTarget.Both)
                {
                    var r = await _transferService.ReuseAsync(
                        new TransferRecord { Text = text, Direction = "介入" },
                        LeftWebView, LeftUrlTextBox.Text, true, text);
                    SetStatus($"介入送信(左): {r.Message}");
                }
                if (win.Target == InterventionTarget.Right || win.Target == InterventionTarget.Both)
                {
                    var r = await _transferService.ReuseAsync(
                        new TransferRecord { Text = text, Direction = "介入" },
                        RightWebView, RightUrlTextBox.Text, true, text);
                    SetStatus($"介入送信(右): {r.Message}");
                }
            }

            // 再開 (ShouldSendかどうかに関わらず再開)
            _autoDebateService.Resume();
            PauseResumeButton.Content = "一時停止";
        }

        // FR-06 役割設定
        private void RoleSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var win = new RoleSettingsWindow(_leftSystemPrompt, _rightSystemPrompt) { Owner = this };
            if (win.ShowDialog() == true)
            {
                _leftSystemPrompt  = win.LeftPrompt;
                _rightSystemPrompt = win.RightPrompt;
                SetStatus("役割設定を更新しました。");
            }
        }

        private void UpdateDebateButtons(bool running)
        {
            StartAutoDebateButton.IsEnabled = !running;
            StopAutoDebateButton.IsEnabled  =  running;
            PauseResumeButton.IsEnabled     =  running;
            InterventionButton.IsEnabled    =  running;  // FR-08
            if (!running) PauseResumeButton.Content = "一時停止";
        }

        // ---------------------------------------------------------------
        // エクスポート
        // ---------------------------------------------------------------
        private async void ExportMarkdownButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = await _sessionRepo.ExportMarkdownAsync();
                SetStatus($"Markdown 保存完了: {Path.GetFileName(path)}");
                RevealInExplorer(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Markdown 保存に失敗しました。\n{ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ExportJsonButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = await _sessionRepo.ExportJsonAsync();
                SetStatus($"JSON 保存完了: {Path.GetFileName(path)}");
                RevealInExplorer(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"JSON 保存に失敗しました。\n{ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenSessionFolderButton_Click(object sender, RoutedEventArgs e)
        {
            string folder = _sessionRepo.SessionFolder;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            SetStatus($"フォルダを開きました: {folder}");
        }

        private static void RevealInExplorer(string filePath)
            => Process.Start(new ProcessStartInfo
               { FileName = "explorer.exe", Arguments = $"/select,\"{filePath}\"", UseShellExecute = true });
    }
}
