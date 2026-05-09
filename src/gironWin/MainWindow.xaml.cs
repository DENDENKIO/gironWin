using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace gironWin
{
    public partial class MainWindow : Window
    {
        // ---------------------------------------------------------------
        // サービス / 状態
        // ---------------------------------------------------------------
        private readonly AiSiteAdapterResolver _adapterResolver   = new();
        private readonly LogRepository         _logRepository     = new();
        private readonly SessionRepository     _sessionRepository = new();
        private readonly QuoteService          _quoteService      = new();
        private          ApprovalPolicy        _approvalPolicy    = ApprovalPolicy.Default;
        private          ApprovalQueue?        _approvalQueue;
        private          TransferService?      _transferService;
        private          AutoDebateService?    _debateService;

        private          DebatePreset?         _currentPreset;
        private          PromptProfile         _promptProfile = PromptProfile.Default;
        private readonly ObservableCollection<TransferRecord> _turnRecords = new();

        // ---------------------------------------------------------------
        // コンストラクタ
        // ---------------------------------------------------------------
        public MainWindow()
        {
            InitializeComponent();
            TurnLogListBox.ItemsSource = _turnRecords;
        }

        // ---------------------------------------------------------------
        // WebView ナビゲーション完了
        // ---------------------------------------------------------------
        private void WebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (sender is Microsoft.Web.WebView2.Wpf.WebView2 wv)
            {
                string url = wv.Source?.ToString() ?? string.Empty;
                var adapter = _adapterResolver.Resolve(url);
                string name = adapter?.SiteName ?? url;

                if (wv == LeftWebView)
                {
                    LeftSiteLabel.Text  = $"左席: {name}";
                    LeftUrlTextBox.Text = url;
                }
                else
                {
                    RightSiteLabel.Text  = $"右席: {name}";
                    RightUrlTextBox.Text = url;
                }
            }
        }

        // ---------------------------------------------------------------
        // メニュー
        // ---------------------------------------------------------------
        private async void MenuExportLog_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter   = "Markdown|*.md|JSON|*.json|テキスト|*.txt",
                FileName = $"giron_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            if (dlg.ShowDialog() != true) return;

            var exportService = new ExportService();
            var records       = _sessionRepository.ToTransferRecords();
            var quotes        = (System.Collections.Generic.IReadOnlyList<QuoteReference>)
                                _quoteService.References;
            var tags          = _sessionRepository.ResearchTags;
            string topic      = TopicTextBox?.Text?.Trim() ?? string.Empty;

            string tempPath;
            if (dlg.FileName.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
                tempPath = await exportService.ExportJsonAsync(records, quotes, tags, _currentPreset, topic);
            else if (dlg.FileName.EndsWith(".txt", System.StringComparison.OrdinalIgnoreCase))
                tempPath = await exportService.ExportTxtAsync(records, topic);
            else
                tempPath = await exportService.ExportMarkdownAsync(records, quotes, tags, _currentPreset, topic);

            System.IO.File.Copy(tempPath, dlg.FileName, overwrite: true);
            SetStatus($"エクスポート完了: {dlg.FileName}");
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

        private void MenuToggleLog_Click(object sender, RoutedEventArgs e)
        {
            LogColumn.Width = LogColumn.Width.Value > 10
                ? new GridLength(0)
                : new GridLength(280);
        }

        // ---------------------------------------------------------------
        // ロール設定
        // ---------------------------------------------------------------
        private void MenuRoleSettings_Click(object sender, RoutedEventArgs e)
        {
            var win = new RoleSettingsWindow(_promptProfile) { Owner = this };
            if (win.ShowDialog() == true && win.ResultProfile != null)
            {
                _promptProfile = win.ResultProfile;
                if (TopicTextBox != null)
                    TopicTextBox.Text = _promptProfile.Topic;
                SetStatus($"ロール設定を更新しました（{_promptProfile.LeftName} vs {_promptProfile.RightName}）");
            }
        }

        // ---------------------------------------------------------------
        // 介入
        // ---------------------------------------------------------------
        private void MenuIntervention_Click(object sender, RoutedEventArgs e)
            => OpenInterventionWindow();

        private void InterventionButton_Click(object sender, RoutedEventArgs e)
            => OpenInterventionWindow();

        private void OpenInterventionWindow()
        {
            if (_debateService == null || !_debateService.IsRunning)
            {
                MessageBox.Show(
                    "\u8a0e\u8ad6\u3092\u958b\u59cb\u3057\u3066\u304b\u3089\u4ecb\u5165\u3057\u3066\u304f\u3060\u3055\u3044\u3002",
                    "\u4ecb\u5165", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _debateService.Pause();

            var win = new InterventionWindow { Owner = this };
            if (win.ShowDialog() == true)
            {
                if (win.ShouldSend && !string.IsNullOrWhiteSpace(win.Text))
                {
                    _ = InjectInterventionAsync(win.Text, win.Target);
                    return; // InjectInterventionAsync \u5185\u3067 Resume() \u3059\u308b
                }
            }

            // \u30ad\u30e3\u30f3\u30bb\u30eb \u307e\u305f\u306f ResumeOnly
            _debateService.Resume();
        }

        private async System.Threading.Tasks.Task InjectInterventionAsync(
            string text, InterventionTarget target)
        {
            async System.Threading.Tasks.Task SendTo(
                Microsoft.Web.WebView2.Wpf.WebView2 wv, string url)
            {
                var adapter = _adapterResolver.Resolve(url);
                if (adapter == null) return;
                bool ok = await adapter.SetInputAsync(wv, text);
                if (ok) await adapter.SendAsync(wv);
            }

            if (target == InterventionTarget.Left || target == InterventionTarget.Both)
                await SendTo(LeftWebView, LeftUrlTextBox.Text.Trim());
            if (target == InterventionTarget.Right || target == InterventionTarget.Both)
                await SendTo(RightWebView, RightUrlTextBox.Text.Trim());

            SetStatus($"介入テキストを送信しました ({target})");
            _debateService?.Resume();
        }

        // ---------------------------------------------------------------
        // プリセット選択
        // ---------------------------------------------------------------
        private void MenuPreset_Click(object sender, RoutedEventArgs e)
        {
            var win = new PresetSelectorWindow { Owner = this };
            if (win.ShowDialog() == true && win.SelectedPreset != null)
            {
                ApplyPreset(win.SelectedPreset);
            }
        }

        private void ApplyPreset(DebatePreset preset)
        {
            _currentPreset = preset;
            CurrentPresetLabel.Text = $"📋 {preset.Name}";
            PolicyLabel.Text        = $"Policy: {preset.TurnPolicy}";

            // 研究モード ON/OFF
            ResearchModeCheckBox.IsChecked = preset.ResearchMode;
            ResearchNoteButton.IsEnabled   = preset.ResearchMode;
            MenuResearchNote.IsEnabled     = preset.ResearchMode;
            ResearchStatusItem.Visibility  = preset.ResearchMode ? Visibility.Visible : Visibility.Collapsed;

            SetStatus($"プリセット '{preset.Name}' を適用しました。");
        }

        // ---------------------------------------------------------------
        // 研究ノート
        // ---------------------------------------------------------------
        private void MenuResearchNote_Click(object sender, RoutedEventArgs e)
        {
            if (_debateService == null)
            {
                MessageBox.Show("討論を開始してから研究ノートを表示してください。",
                    "研究ノート", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var win = new ResearchNoteWindow(_debateService.ResearchService) { Owner = this };
            win.Show();
        }

        // ---------------------------------------------------------------
        // 第3席パネル操作
        // ---------------------------------------------------------------
        private void ThirdSeatModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThirdSeatUrlLabel == null) return;
            bool isAiSite = (ThirdSeatModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "AiSite";
            ThirdSeatUrlLabel.Visibility   = isAiSite ? Visibility.Visible   : Visibility.Collapsed;
            ThirdSeatUrlTextBox.Visibility = isAiSite ? Visibility.Visible   : Visibility.Collapsed;
        }

        private void ResearchModeCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool on = ResearchModeCheckBox.IsChecked == true;
            ResearchNoteButton.IsEnabled  = on;
            MenuResearchNote.IsEnabled    = on;
            ResearchStatusItem.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---------------------------------------------------------------
        // 討論制御ボタン
        // ---------------------------------------------------------------
        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_debateService?.IsRunning == true) return;

            // サービス初期化
            _approvalQueue   = new ApprovalQueue();
            _transferService = new TransferService(_adapterResolver, _turnRecords);
            _sessionRepository.StartNewSession();
            _quoteService.Clear();   // セッション開始時に引用クリア
            _debateService   = new AutoDebateService(
                _transferService, _approvalQueue, _adapterResolver,
                _logRepository, _sessionRepository);

            // イベント接続
            _debateService.StatusChanged     += DebateService_StatusChanged;
            _debateService.TurnAdvanced      += DebateService_TurnAdvanced;
            _debateService.DebateStopped     += DebateService_DebateStopped;
            _debateService.ThirdSeatInputRequired += DebateService_ThirdSeatInputRequired;
            _debateService.ResearchTagsExtracted  += DebateService_ResearchTagsExtracted;

            // 承認キューのイベント接続
            _approvalQueue.ApprovalRequested += ApprovalQueue_ApprovalRequested;

            _turnRecords.Clear();
            var config = BuildConfig();
            _debateService.Start(config);

            StartButton.IsEnabled         = false;
            StopButton.IsEnabled          = true;
            PauseResumeButton.IsEnabled   = true;
            InterventionButton.IsEnabled  = true;
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _debateService?.Stop();
        }

        private void PauseResumeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_debateService == null) return;
            if (_debateService.IsPaused)
            {
                _debateService.Resume();
                PauseResumeButton.Content = "⏸ 一時停止";
            }
            else
            {
                _debateService.Pause();
                PauseResumeButton.Content = "▶ 再開";
            }
        }

        // ---------------------------------------------------------------
        // AutoDebateConfig 組み立て
        // ---------------------------------------------------------------
        private AutoDebateConfig BuildConfig()
        {
            int.TryParse(MaxTurnsTextBox.Text, out int maxTurns);
            int.TryParse(ThirdSeatIntervalTextBox.Text, out int interval);
            if (interval <= 0) interval = 2;

            var thirdMode = (ThirdSeatModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
            {
                "Human"   => ThirdSeatMode.Human,
                "AiSite"  => ThirdSeatMode.AiSite,
                _         => ThirdSeatMode.Disabled
            };

            var thirdRole = (ThirdSeatRoleCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
            {
                "Reviewer"   => DebateRole.Reviewer,
                "Researcher" => DebateRole.Researcher,
                "Critic"     => DebateRole.Critic,
                _            => DebateRole.Moderator
            };

            var thirdSeat = new ThirdSeatConfig
            {
                Mode          = thirdMode,
                Role          = thirdRole,
                DisplayName   = ThirdSeatNameTextBox.Text.Trim().NullIfEmpty() ?? "第3席",
                IntervalTurns = interval,
                Url           = ThirdSeatUrlTextBox.Text.Trim(),
                WebView       = thirdMode == ThirdSeatMode.AiSite ? null : null // 将来: 第3席WebView
            };

            _approvalPolicy = RequireApprovalCheckBox.IsChecked == true
                ? ApprovalPolicy.Default
                : ApprovalPolicy.FullAuto;

            var policy = _currentPreset?.TurnPolicy ?? TurnPolicy.RoundRobin;

            // プロンプト: プリセット優先 → PromptProfile にフォールバック
            string leftPrompt  = !string.IsNullOrWhiteSpace(_currentPreset?.LeftPrompt)
                ? _currentPreset.LeftPrompt
                : _promptProfile.LeftSystemPrompt;
            string rightPrompt = !string.IsNullOrWhiteSpace(_currentPreset?.RightPrompt)
                ? _currentPreset.RightPrompt
                : _promptProfile.RightSystemPrompt;

            string topic = TopicTextBox?.Text?.Trim()
                           ?? _promptProfile.Topic;

            var config = new AutoDebateConfig
            {
                LeftWebView         = LeftWebView,
                RightWebView        = RightWebView,
                LeftUrl             = LeftUrlTextBox.Text.Trim(),
                RightUrl            = RightUrlTextBox.Text.Trim(),
                RequireApproval     = RequireApprovalCheckBox.IsChecked == true,
                ApprovalPolicy      = _approvalPolicy,
                AppendBridge        = AppendBridgeCheckBox.IsChecked    == true,
                MaxTurns            = maxTurns,
                TurnIntervalMs      = 500,
                PostSendWaitMs      = 5000,
                GenerationTimeoutMs = 90000,
                TurnPolicy          = policy,
                ResearchMode        = ResearchModeCheckBox.IsChecked == true,
                ThirdSeat           = thirdSeat,
                LeftSystemPrompt    = leftPrompt,
                RightSystemPrompt   = rightPrompt,
                Topic               = topic
            };

            PolicyLabel.Text = $"Policy: {policy}";
            return config;
        }

        // ---------------------------------------------------------------
        // AutoDebateService イベント
        // ---------------------------------------------------------------
        private void DebateService_StatusChanged(object? sender, string msg)
            => Dispatcher.Invoke(() => SetStatus(msg));

        private void DebateService_TurnAdvanced(object? sender, int turn)
            => Dispatcher.Invoke(() => TurnCountLabel.Text = $"Turn: {turn}");

        private void DebateService_DebateStopped(object? sender, EventArgs e)
            => Dispatcher.Invoke(() =>
            {
                StartButton.IsEnabled        = true;
                StopButton.IsEnabled         = false;
                PauseResumeButton.IsEnabled  = false;
                PauseResumeButton.Content    = "⏸ 一時停止";
                InterventionButton.IsEnabled = false;
                ApprovalPanel.Visibility     = Visibility.Collapsed;
            });

        private void DebateService_ThirdSeatInputRequired(object? sender, ThirdSeatInputRequest req)
            => Dispatcher.Invoke(() =>
            {
                var win = new ThirdSeatWindow(req) { Owner = this };
                win.Show(); // モーダルレスで表示（入力後に自動コールバック）
            });

        private void DebateService_ResearchTagsExtracted(object? sender, System.Collections.Generic.List<ResearchTagEntry> tags)
            => Dispatcher.Invoke(() =>
            {
                if (_debateService == null) return;
                int total = _debateService.ResearchService.Entries.Count;
                ResearchTagCountLabel.Text  = $"🔬 タグ: {total}";
                ResearchNoteButton.IsEnabled = true;
                MenuResearchNote.IsEnabled   = true;
                SetStatus($"研究タグ {tags.Count} 件を抽出しました（合計 {total} 件）");
            });

        // ---------------------------------------------------------------
        // 承認キューイベント
        // ---------------------------------------------------------------
        private void ApprovalQueue_ApprovalRequested(object? sender, ApprovalRequestedEventArgs e)
            => Dispatcher.Invoke(() =>
            {
                ApprovalTitleLabel.Text    = "承認待ち";
                ApprovalDirectionLabel.Text = e.Direction;
                ApprovalTextBox.Text       = e.Text;
                ApprovalPanel.Visibility   = Visibility.Visible;
                ApprovalTextBox.Focus();
            });

        private void ApproveButton_Click(object sender, RoutedEventArgs e)
        {
            _approvalQueue?.Approve(ApprovalTextBox.Text);
            ApprovalPanel.Visibility = Visibility.Collapsed;
        }

        private void RejectButton_Click(object sender, RoutedEventArgs e)
        {
            _approvalQueue?.Reject();
            ApprovalPanel.Visibility = Visibility.Collapsed;
        }

        // ---------------------------------------------------------------
        // ログパネル選択時のプレビュー
        // ---------------------------------------------------------------
        private void TurnLogListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TurnLogListBox.SelectedItem is TransferRecord rec)
            {
                var win = new TextPreviewWindow(
                    rec,
                    _quoteService,
                    rec.Direction,
                    _sessionRepository)   // ★ sessionRepository を渡す
                {
                    Owner = this,
                    Title = $"Turn {rec.TurnNumber} [{rec.Direction}]"
                };
                win.Show();
            }
        }

        // ---------------------------------------------------------------
        // ウィンドウクローズ
        // ---------------------------------------------------------------
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _debateService?.Stop();
        }

        // ---------------------------------------------------------------
        // ヘルパー
        // ---------------------------------------------------------------
        private void SetStatus(string msg)
            => StatusTextBlock.Text = $"[{DateTime.Now:HH:mm:ss}] {msg}";
    }

    internal static class StringExtensions
    {
        public static string? NullIfEmpty(this string? s)
            => string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
