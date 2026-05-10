using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
        private IReadOnlyList<TransferRecord>? _lastTurnRecords;
        private ResearchModeService?           _lastResearchService;

        // デバッグログウィンドウ
        private readonly DebugLogWindow _debugLogWindow;

        // ---------------------------------------------------------------
        // コンストラクタ
        // ---------------------------------------------------------------
        public MainWindow()
        {
            InitializeComponent();
            TurnLogListBox.ItemsSource = _turnRecords;

            // DebugLogWindow は Owner 設定なしで生成（初回表示時に遅延設定）
            _debugLogWindow = new DebugLogWindow();

            // 7日以上前の HTML スナップショットをクリーンアップ
            HtmlSnapshotStore.Cleanup(TimeSpan.FromDays(7));

            AppLogger.Info(LogCategory.System, "MainWindow 初期化完了");
            UpdateRoleplayUiState();
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

                AppLogger.Debug(LogCategory.System,
                    $"WebView NavigationCompleted url={url} adapter={name}");

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
        private void MenuExportLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new LogExportOptionsDialog { Owner = this };
                if (dialog.ShowDialog() != true || dialog.Options == null) return;

                var records = _sessionRepository.ToTransferRecords();
                if (records == null || records.Count == 0)
                {
                    MessageBox.Show(this, "エクスポート対象のログがありません。", "エクスポート", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var exportService = new ExportService();
                var result = exportService.ExportAiSiteHtmlTabLogs(records, dialog.Options);

                MessageBox.Show(
                    this,
                    result.Message,
                    "エクスポート",
                    MessageBoxButton.OK,
                    result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"エクスポート中にエラーが発生しました。\n{ex.Message}", "エクスポート", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

        private void MenuToggleLog_Click(object sender, RoutedEventArgs e)
        {
            LogColumn.Width = LogColumn.Width.Value > 10
                ? new GridLength(0)
                : new GridLength(280);
        }

        private void MenuDebugLog_Click(object sender, RoutedEventArgs e)
            => OpenDebugLogWindow();

        private void OpenDebugLogWindow()
        {
            if (_debugLogWindow.Owner == null && IsLoaded)
                _debugLogWindow.Owner = this;

            _debugLogWindow.Show();
            _debugLogWindow.Activate();
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
                AppLogger.Info(LogCategory.System,
                    $"ロール設定更新 Left={_promptProfile.LeftName} Right={_promptProfile.RightName}");
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
                    "討論を開始してから介入してください。",
                    "介入", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _debateService.Pause();

            var win = new InterventionWindow { Owner = this };
            if (win.ShowDialog() == true)
            {
                if (win.ShouldSend && !string.IsNullOrWhiteSpace(win.Text))
                {
                    AppLogger.Info(LogCategory.System,
                        $"介入テキスト送信 target={win.Target} len={win.Text.Length}");
                    _ = InjectInterventionAsync(win.Text, win.Target);
                    return;
                }
            }

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

        private void ClearPresetButton_Click(object sender, RoutedEventArgs e)
        {
            _currentPreset = null;
            CurrentPresetLabel.Text = "(プリセット未選択)";
            PolicyLabel.Text = $"Policy: {TurnPolicy.RoundRobin}";

            UpdateRoleplayUiState();

            AppLogger.Info(LogCategory.System, "プリセット解除");
            SetStatus("プリセットを解除しました。");
        }

        private void ApplyPreset(DebatePreset preset)
        {
            _currentPreset = preset;
            CurrentPresetLabel.Text = $"📋 {preset.Name}";
            PolicyLabel.Text        = $"Policy: {preset.TurnPolicy}";

            ResearchModeCheckBox.IsChecked = preset.ResearchMode;
            ResearchNoteButton.IsEnabled   = preset.ResearchMode;
            MenuResearchNote.IsEnabled     = preset.ResearchMode;
            ResearchStatusItem.Visibility  = preset.ResearchMode ? Visibility.Visible : Visibility.Collapsed;

            UpdateRoleplayUiState();

            AppLogger.Info(LogCategory.System,
                $"プリセット適用 name={preset.Name} policy={preset.TurnPolicy} research={preset.ResearchMode}");
            SetStatus($"プリセット '{preset.Name}' を適用しました。");
        }

        private void UpdateRoleplayUiState()
        {
            bool enabled = _currentPreset == null;

            if (LeftPersonaTextBox != null) LeftPersonaTextBox.IsEnabled = enabled;
            if (RightPersonaTextBox != null) RightPersonaTextBox.IsEnabled = enabled;
            if (LeftRoleplayCheckBox != null) LeftRoleplayCheckBox.IsEnabled = enabled;
            if (RightRoleplayCheckBox != null) RightRoleplayCheckBox.IsEnabled = enabled;

            if (!enabled)
            {
                LeftRoleplayCheckBox.IsChecked = false;
                RightRoleplayCheckBox.IsChecked = false;
            }
        }

        private static string BuildRoleplayPrompt(string personaText, bool enabled, string sideLabel)
        {
            if (!enabled || string.IsNullOrWhiteSpace(personaText))
                return string.Empty;

            return
                $"【なりきり設定】あなたは以下の人物像になりきって応答してください。\n" +
                $"役割名: {sideLabel}\n" +
                $"人物像:\n{personaText.Trim()}\n\n" +
                $"口調・視点・知識レベル・価値観・関心において、この人物像を反映してください。" +
                $"ただし議題から逸れず、応答内容として自然な文章で出力してください。";
        }

        private void UrlTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (sender is not TextBox tb) return;

            string url = tb.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(url)) return;

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
                tb.Text = url;
            }

            try
            {
                if (tb == LeftUrlTextBox)
                {
                    LeftWebView.Source = new Uri(url);
                    AppLogger.Info(LogCategory.System, $"左URL再遷移 url={url}");
                    SetStatus($"左サイトを再読み込みしました: {url}");
                }
                else if (tb == RightUrlTextBox)
                {
                    RightWebView.Source = new Uri(url);
                    AppLogger.Info(LogCategory.System, $"右URL再遷移 url={url}");
                    SetStatus($"右サイトを再読み込みしました: {url}");
                }

                e.Handled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"URL が不正です。\n{ex.Message}",
                    "ナビゲーションエラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        // ---------------------------------------------------------------
        // 研究ノート
        // ---------------------------------------------------------------
        private void MenuResearchNote_Click(object sender, RoutedEventArgs e)
        {
            // 実行中は _debateService から、終了後は _lastResearchService を使う
            var service = _debateService?.ResearchService ?? _lastResearchService;

            if (service == null)
            {
                MessageBox.Show("研究ノートがありません。\n研究モードで討論を実行してから使用してください。",
                    "研究ノート", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var win = new ResearchNoteWindow(service) { Owner = this };
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

            _approvalQueue   = new ApprovalQueue();
            _transferService = new TransferService(_adapterResolver, _turnRecords);
            _sessionRepository.StartNewSession();
            _quoteService.Clear();
            _debateService   = new AutoDebateService(
                _transferService, _approvalQueue, _adapterResolver,
                _logRepository);

            // ★ 追加: TransferService の内部ログを AppLogger に接続
            _transferService.DebugLog += (_, msg) =>
                AppLogger.Debug(LogCategory.Transfer, msg);

            // ★ 追加: AutoDebateService の StatusChanged を AppLogger に接続
            _debateService.StatusChanged += (_, msg) =>
                AppLogger.Info(LogCategory.RunLoop, msg);

            _debateService.StatusChanged              += DebateService_StatusChanged;
            _debateService.TurnAdvanced               += DebateService_TurnAdvanced;
            _debateService.DebateStopped              += DebateService_DebateStopped;
            _debateService.ThirdSeatInputRequired     += DebateService_ThirdSeatInputRequired;
            _debateService.HumanPriorityInputRequired += DebateService_HumanPriorityInputRequired;
            _debateService.ResearchTagsExtracted      += DebateService_ResearchTagsExtracted;

            _approvalQueue.ApprovalRequested += ApprovalQueue_ApprovalRequested;

            _turnRecords.Clear();

            AppLogger.Info(LogCategory.System,
                $"===== 討論開始 ===== MaxTurns={MaxTurnsTextBox.Text} " +
                $"Left={LeftUrlTextBox.Text.Trim()} Right={RightUrlTextBox.Text.Trim()}");

            var config = BuildConfig();
            _debateService.Start(config);

            StartButton.IsEnabled         = false;
            StopButton.IsEnabled          = true;
            PauseResumeButton.IsEnabled   = true;
            InterventionButton.IsEnabled  = true;
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            AppLogger.Info(LogCategory.System, "停止ボタン押下");
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
                WebView       = null
            };

            _approvalPolicy = RequireApprovalCheckBox.IsChecked == true
                ? ApprovalPolicy.Default
                : ApprovalPolicy.FullAuto;

            var policy = _currentPreset?.TurnPolicy ?? TurnPolicy.RoundRobin;

            string leftPrompt = !string.IsNullOrWhiteSpace(_currentPreset?.LeftPrompt)
                ? _currentPreset.LeftPrompt
                : _promptProfile.LeftSystemPrompt;

            string rightPrompt = !string.IsNullOrWhiteSpace(_currentPreset?.RightPrompt)
                ? _currentPreset.RightPrompt
                : _promptProfile.RightSystemPrompt;

            if (_currentPreset == null)
            {
                string leftRoleplay = BuildRoleplayPrompt(
                    LeftPersonaTextBox.Text,
                    LeftRoleplayCheckBox.IsChecked == true,
                    string.IsNullOrWhiteSpace(LeftPersonaTextBox.Text) ? "左席" : LeftPersonaTextBox.Text.Trim());

                string rightRoleplay = BuildRoleplayPrompt(
                    RightPersonaTextBox.Text,
                    RightRoleplayCheckBox.IsChecked == true,
                    string.IsNullOrWhiteSpace(RightPersonaTextBox.Text) ? "右席" : RightPersonaTextBox.Text.Trim());

                if (!string.IsNullOrWhiteSpace(leftRoleplay))
                    leftPrompt = string.IsNullOrWhiteSpace(leftPrompt)
                        ? leftRoleplay
                        : leftPrompt + "\n\n" + leftRoleplay;

                if (!string.IsNullOrWhiteSpace(rightRoleplay))
                    rightPrompt = string.IsNullOrWhiteSpace(rightPrompt)
                        ? rightRoleplay
                        : rightPrompt + "\n\n" + rightRoleplay;
            }

            string topic = TopicTextBox?.Text?.Trim()
                           ?? _promptProfile.Topic;

            AppLogger.Debug(LogCategory.System,
                $"Config 組み立て maxTurns={maxTurns} policy={policy} " +
                $"thirdMode={thirdMode} thirdInterval={interval} " +
                $"approvalPolicy={_approvalPolicy} appendBridge={AppendBridgeCheckBox.IsChecked} " +
                $"leftRoleplay={LeftRoleplayCheckBox.IsChecked == true} rightRoleplay={RightRoleplayCheckBox.IsChecked == true}");

            var config = new AutoDebateConfig
            {
                LeftWebView            = LeftWebView,
                RightWebView           = RightWebView,
                LeftUrl                = LeftUrlTextBox.Text.Trim(),
                RightUrl               = RightUrlTextBox.Text.Trim(),
                RequireApproval        = RequireApprovalCheckBox.IsChecked == true,
                ApprovalPolicy         = _approvalPolicy,
                AppendBridge           = AppendBridgeCheckBox.IsChecked == true,
                MaxTurns               = maxTurns,
                TurnIntervalMs         = 500,
                HumanPriorityTimeoutMs = 10000,
                PostSendWaitMs         = 5000,
                GenerationTimeoutMs    = 90000,
                TurnPolicy             = policy,
                ResearchMode           = ResearchModeCheckBox.IsChecked == true,
                ThirdSeat              = thirdSeat,
                LeftSystemPrompt       = leftPrompt,
                RightSystemPrompt      = rightPrompt,
                Topic                  = topic,
                LogRecords             = _turnRecords
            };

            PolicyLabel.Text = $"Policy: {policy}";
            return config;
        }

        // ---------------------------------------------------------------
        // AutoDebateService イベント
        // ---------------------------------------------------------------
        private void DebateService_StatusChanged(object? sender, string msg)
            => Dispatcher.Invoke(() => SetStatus(msg));

        private void DebateService_TurnAdvanced(object? sender, int count)
            => Dispatcher.Invoke(() => TurnCountLabel.Text = $"往復: {count}");

        private void DebateService_DebateStopped(object? sender, EventArgs e)
            => Dispatcher.Invoke(() =>
            {
                StartButton.IsEnabled        = true;
                StopButton.IsEnabled         = false;
                PauseResumeButton.IsEnabled  = false;
                PauseResumeButton.Content    = "⏸ 一時停止";
                InterventionButton.IsEnabled = false;
                ApprovalPanel.Visibility     = Visibility.Collapsed;
                AppLogger.Info(LogCategory.System, "討論停止イベント受信 → UI更新");

                // ── 追加：終了時点のログを退避する ──
                _lastTurnRecords    = _turnRecords.ToList();
                _lastResearchService = _debateService?.ResearchService;
            });

        /// <summary>第3席 Human モード：入力ダイアログを表示して OnInputReady を呼ぶ。</summary>
        private void DebateService_ThirdSeatInputRequired(object? sender, ThirdSeatInputRequest req)
            => Dispatcher.Invoke(() =>
            {
                AppLogger.Info(LogCategory.System,
                    $"第3席入力要求 DisplayName={req.DisplayName} Role={req.Role}");
                var win = new ThirdSeatWindow(req) { Owner = this };
                win.Show();
            });

        /// <summary>
        /// HumanPriority モード：割り込み入力ダイアログを表示する。
        /// タイムアウト前にユーザーが送信すれば req.OnInputReady(text) を呼ぶ。
        /// タイムアウトすれば req.OnTimeout() を呼ぶ（討論ループ側でスキップ）。
        /// </summary>
        private void DebateService_HumanPriorityInputRequired(object? sender, HumanPriorityInputRequest req)
            => Dispatcher.Invoke(() =>
            {
                AppLogger.Info(LogCategory.System,
                    $"HumanPriority 入力要求 TimeoutMs={req.TimeoutMs}");
                var win = new HumanPriorityWindow(req) { Owner = this };
                win.Show();
            });

        private void DebateService_ResearchTagsExtracted(object? sender, ResearchTagsExtractedEventArgs e)
            => Dispatcher.Invoke(() =>
            {
                ResearchStatusItem.Content = $"🔬 研究タグ: {e.TotalCount} 件";
                AppLogger.Debug(LogCategory.System,
                    $"研究タグ抽出 total={e.TotalCount} new={e.NewTags.Count}");
            });

        // ---------------------------------------------------------------
        // ApprovalQueue イベント
        // ---------------------------------------------------------------
        private void ApprovalQueue_ApprovalRequested(object? sender, ApprovalRequestedEventArgs e)
            => Dispatcher.Invoke(() =>
            {
                AppLogger.Info(LogCategory.System,
                    $"承認要求 src={e.Source} tgt={e.Target} len={e.Text.Length}");

                ApprovalSourceLabel.Text = $"送信元: {e.Source} → {e.Target}";
                ApprovalTextBox.Text     = e.Text;
                ApprovalPanel.Visibility = Visibility.Visible;
            });

        private void ApproveButton_Click(object sender, RoutedEventArgs e)
        {
            string editedText = ApprovalTextBox.Text;
            AppLogger.Info(LogCategory.System,
                $"承認ボタン押下 editedLen={editedText.Length}");
            ApprovalPanel.Visibility = Visibility.Collapsed;
            _approvalQueue?.Approve(editedText);
        }

        private void RejectButton_Click(object sender, RoutedEventArgs e)
        {
            AppLogger.Info(LogCategory.System, "却下ボタン押下");
            ApprovalPanel.Visibility = Visibility.Collapsed;
            _approvalQueue?.Reject();
        }

        // ---------------------------------------------------------------
        // 研究ノートボタン
        // ---------------------------------------------------------------
        private void ResearchNoteButton_Click(object sender, RoutedEventArgs e)
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
        // 司会サマリー
        // ---------------------------------------------------------------
        private void MenuModeratorSummary_Click(object sender, RoutedEventArgs e)
        {
            // 実行中は _turnRecords を直接参照、終了後は _lastTurnRecords を使う
            var records = (_turnRecords.Count > 0)
                ? (IReadOnlyList<TransferRecord>)_turnRecords
                : _lastTurnRecords;

            if (records == null || records.Count == 0)
            {
                MessageBox.Show("討論ログがありません。\n討論を実行してから使用してください。",
                    "司会サマリー", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var win = new ModeratorSummaryWindow(records) { Owner = this };
            win.Show();
        }

        // ---------------------------------------------------------------
        // ターンログ クリック → テキストプレビュー
        // ---------------------------------------------------------------
        private void TurnLogListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TurnLogListBox.SelectedItem is not TransferRecord rec) return;

            int selectedIndex = TurnLogListBox.SelectedIndex;
            var records = _turnRecords.ToList();

            // 選択解除（同じ行を再クリックしてもイベントが発火するよう）
            TurnLogListBox.UnselectAll();

            // ★ 外部の WinUI 3 ログリーダーを起動
            LogReaderLauncher.Open(records, selectedIndex, _quoteService);
        }

        // ---------------------------------------------------------------
        // ウィンドウクローズ
        // ---------------------------------------------------------------
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            AppLogger.Info(LogCategory.System, "MainWindow クローズ → 討論停止");
            _debateService?.Stop();
        }

        // ---------------------------------------------------------------
        // ステータス表示
        // ---------------------------------------------------------------
        private void SetStatus(string msg)
        {
            StatusTextBlock.Text = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        }
    }

    internal static class StringExtensions
    {
        public static string? NullIfEmpty(this string? s)
            => string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
