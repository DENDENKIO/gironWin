using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json;
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
        private readonly LogRepository _logRepository = new();
        private readonly QuoteService _quoteService = new();
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
            _transferService = new TransferService(_adapterResolver, _transferRecords, _logRepository);
            _transferService.DebugLog += (_, msg) =>
            {
                Dispatcher.Invoke(() =>
                {
                    StatusTextBlock.Text = msg;
                    System.Diagnostics.Debug.WriteLine(msg);
                });
            };
            _autoDebateService = new AutoDebateService(
                _transferService, _approvalQueue, _adapterResolver, _logRepository);
            _autoDebateService.StatusChanged  += (_, msg) => Dispatcher.Invoke(() => SetStatus(msg));
            _autoDebateService.TurnAdvanced   += (_, turn) => Dispatcher.Invoke(() =>
            {
                TurnCountTextBlock.Text = $"ターン: {turn}";
                UpdateSessionStats();
            });
            _autoDebateService.DebateStopped  += (_, _) => Dispatcher.Invoke(() =>
            {
                UpdateDebateButtons(false);
                UpdateSessionStats();
            });

            foreach (var adapter in _adapterResolver.Adapters)
            {
                if (adapter is GeminiAdapter gemini)
                {
                    gemini.DebugLog += (s, msg) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            StatusTextBlock.Text = msg;
                            System.Diagnostics.Debug.WriteLine(msg);
                        });
                    };
                }
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

            NavigateTo(LeftWebView, LeftUrlTextBox.Text);
            NavigateTo(RightWebView, RightUrlTextBox.Text);
        }

        private void NavigateTo(Microsoft.Web.WebView2.Wpf.WebView2 webView, string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                webView.Source = uri;
        }

        // ---------------------------------------------------------------
        // ステータス・統計
        // ---------------------------------------------------------------

        private void SetStatus(string message) => StatusTextBlock.Text = message;

        private void UpdateSessionStats()
        {
            var session = _logRepository.Current;
            if (session == null)
            {
                SessionStatsTextBlock.Text = "";
                return;
            }
            SessionStatsTextBlock.Text =
                $"セッション: {session.TotalTurns} ターン / {session.TotalChars} 文字";
        }

        // ---------------------------------------------------------------
        // 転送ヘルパー
        // ---------------------------------------------------------------

        private bool AppendBridge => AppendBridgeCheckBox.IsChecked == true;
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
            string sourceUrl,
            string targetUrl,
            bool submit)
        {
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
                    if (overrideText == null) { SetStatus("転送をキャンセルしました。"); return; }
                }
            }

            var result = await _transferService.TransferAsync(
                sourceWebView, targetWebView,
                sourceUrl, targetUrl,
                submit, AppendBridge, overrideText);

            SetStatus(result.Message);
            UpdateSessionStats();
        }

        private async Task RunReuseAsync(
            TransferRecord? record,
            Microsoft.Web.WebView2.Wpf.WebView2 targetWebView,
            string targetUrl,
            bool submit)
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
        // タイトル・選択テキスト取得
        // ---------------------------------------------------------------

        private async void LeftTitleButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string raw = await LeftWebView.ExecuteScriptAsync("document.title || location.href;");
                string title = JsonSerializer.Deserialize<string>(raw) ?? raw.Trim('"');
                Clipboard.SetText(title);
                SetStatus($"左タイトルをコピーしました: {title}");
            }
            catch (Exception ex)
            {
                SetStatus($"左タイトル取得失敗: {ex.Message}");
            }
        }

        private async void RightTitleButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string raw = await RightWebView.ExecuteScriptAsync("document.title || location.href;");
                string title = JsonSerializer.Deserialize<string>(raw) ?? raw.Trim('"');
                Clipboard.SetText(title);
                SetStatus($"右タイトルをコピーしました: {title}");
            }
            catch (Exception ex)
            {
                SetStatus($"右タイトル取得失敗: {ex.Message}");
            }
        }

        private async void LeftSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var adapter = _adapterResolver.Resolve(LeftUrlTextBox.Text);
                string selected = adapter != null
                    ? await adapter.GetSelectedTextAsync(LeftWebView)
                    : await GetSelectionFallbackAsync(LeftWebView);

                if (string.IsNullOrWhiteSpace(selected))
                {
                    SetStatus("左: 選択テキストがありません。");
                    return;
                }
                Clipboard.SetText(selected);
                SetStatus($"左選択テキストをコピーしました（{selected.Length}文字）");
            }
            catch (Exception ex)
            {
                SetStatus($"左選択取得失敗: {ex.Message}");
            }
        }

        private async void RightSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var adapter = _adapterResolver.Resolve(RightUrlTextBox.Text);
                string selected = adapter != null
                    ? await adapter.GetSelectedTextAsync(RightWebView)
                    : await GetSelectionFallbackAsync(RightWebView);

                if (string.IsNullOrWhiteSpace(selected))
                {
                    SetStatus("右: 選択テキストがありません。");
                    return;
                }
                Clipboard.SetText(selected);
                SetStatus($"右選択テキストをコピーしました（{selected.Length}文字）");
            }
            catch (Exception ex)
            {
                SetStatus($"右選択取得失敗: {ex.Message}");
            }
        }

        /// <summary>アダプタがない場合の window.getSelection() フォールバック</summary>
        private async Task<string> GetSelectionFallbackAsync(Microsoft.Web.WebView2.Wpf.WebView2 webView)
        {
            string raw = await webView.ExecuteScriptAsync("window.getSelection()?.toString() ?? '';");
            return JsonSerializer.Deserialize<string>(raw) ?? raw.Trim('"');
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
        // FR-04: ログ保存
        // ---------------------------------------------------------------

        private void SaveLogJsonButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = _logRepository.SaveCurrentJson();
                SetStatus($"JSON 保存完了: {path}");
                MessageBox.Show($"保存しました:\n{path}", "JSON 保存",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                SetStatus($"JSON 保存失敗: {ex.Message}");
                MessageBox.Show($"保存に失敗しました:\n{ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveLogMdButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = _logRepository.ExportCurrentMarkdown();
                SetStatus($"Markdown 保存完了: {path}");
                MessageBox.Show($"保存しました:\n{path}", "Markdown 保存",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                SetStatus($"Markdown 保存失敗: {ex.Message}");
                MessageBox.Show($"保存に失敗しました:\n{ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ---------------------------------------------------------------
        // FR-04: 引用
        // ---------------------------------------------------------------

        private void QuoteFullButton_Click(object sender, RoutedEventArgs e) =>
            QuoteFull(GetSelectedRecord());

        private void QuoteFullMenuItem_Click(object sender, RoutedEventArgs e) =>
            QuoteFull(GetSelectedRecord());

        private void QuoteFull(TransferRecord? record)
        {
            if (record == null) { SetStatus("引用対象の履歴が選択されていません。"); return; }

            var entry = _logRepository.Current?.Entries
                .FirstOrDefault(e => e.MessageId == record.MessageLogEntryId);

            string quotedText;
            if (entry != null)
            {
                var qref = _quoteService.QuoteFull(entry);
                quotedText = QuoteService.FormatQuoteForSend(qref);
            }
            else
            {
                quotedText = $"> [全文引用 Turn {record.TurnNumber}]\n> {record.Text.Replace("\n", "\n> ")}\n";
            }

            Clipboard.SetText(quotedText);
            SetStatus($"全文引用をクリップボードにコピーしました。({quotedText.Length} 文字)");
        }

        // ---------------------------------------------------------------
        // FR-04: 承認・却下
        // ---------------------------------------------------------------

        private void ApproveButton_Click(object sender, RoutedEventArgs e) =>
            SetApprovalStatus(GetSelectedRecord(), ApprovalStatuses.Approved);

        private void RejectButton_Click(object sender, RoutedEventArgs e) =>
            SetApprovalStatus(GetSelectedRecord(), ApprovalStatuses.Rejected);

        private void ApproveMenuItem_Click(object sender, RoutedEventArgs e) =>
            SetApprovalStatus(GetSelectedRecord(), ApprovalStatuses.Approved);

        private void RejectMenuItem_Click(object sender, RoutedEventArgs e) =>
            SetApprovalStatus(GetSelectedRecord(), ApprovalStatuses.Rejected);

        private void SetApprovalStatus(TransferRecord? record, string status)
        {
            if (record == null) { SetStatus("対象の履歴が選択されていません。"); return; }
            record.ApprovalStatus = status;

            var entry = _logRepository.Current?.Entries
                .FirstOrDefault(e => e.MessageId == record.MessageLogEntryId);
            if (entry != null) entry.ApprovalStatus = status;

            int idx = _transferRecords.IndexOf(record);
            if (idx >= 0)
            {
                _transferRecords.RemoveAt(idx);
                _transferRecords.Insert(idx, record);
                TransferHistoryListView.SelectedIndex = idx;
            }

            SetStatus($"承認状態を [{status}] に変更しました。");
        }

        // ---------------------------------------------------------------
        // FR-04: 段落一覧表示
        // ---------------------------------------------------------------

        private void ShowParagraphsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var record = GetSelectedRecord();
            if (record == null) { SetStatus("対象の履歴が選択されていません。"); return; }

            if (record.ParagraphBlocks.Count == 0)
            {
                MessageBox.Show("段落情報がありません。", "段落一覧",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"発言: {record.Direction} (Turn {record.TurnNumber})");
            sb.AppendLine($"段落数: {record.ParagraphBlocks.Count}");
            sb.AppendLine();
            foreach (var p in record.ParagraphBlocks)
            {
                sb.AppendLine($"[段落 {p.Index + 1}] ({p.CharStart}-{p.CharEnd})");
                sb.AppendLine(p.Text);
                sb.AppendLine();
            }

            var win = new TextPreviewWindow(sb.ToString())
            {
                Owner = this,
                Title = $"段落一覧 - {record.Direction}"
            };
            win.ShowDialog();
        }

        // ---------------------------------------------------------------
        // 自動討論
        // ---------------------------------------------------------------

        private void StartAutoDebateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_autoDebateService.IsRunning) return;

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
                Topic = TopicTextBox.Text.Trim()
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

        private void UpdateDebateButtons(bool running)
        {
            StartAutoDebateButton.IsEnabled = !running;
            StopAutoDebateButton.IsEnabled = running;
            PauseResumeButton.IsEnabled = running;
            if (!running) PauseResumeButton.Content = "一時停止";
        }
    }
}
