using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace gironWin
{

    /// <summary>
    /// Phase 3-5: 第3席・TurnPolicy・ResearchMode 対応 AutoDebateService
    /// </summary>
    public sealed class AutoDebateService
    {
        private readonly TransferService _transferService;
        private readonly ApprovalQueue _approvalQueue;
        private readonly AiSiteAdapterResolver _adapterResolver;
        private readonly SummaryService _summaryService = new();
        private readonly ResearchModeService _researchService = new();
        private readonly LogRepository _logRepository;

        private CancellationTokenSource? _cts;
        private bool _isPaused;
        private TaskCompletionSource<bool>? _pauseTcs;

        // HumanPriority 入力待機用
        private TaskCompletionSource<string?>? _humanInputTcs;

        public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;
        public bool IsPaused => _isPaused;

        public ResearchModeService ResearchService => _researchService;
        public SummaryService SummaryService => _summaryService;

        public event EventHandler<string>? StatusChanged;
        public event EventHandler<int>? TurnAdvanced;
        public event EventHandler? DebateStopped;

        /// <summary>第3席への入力が必要なとき発火（Human モード）</summary>
        public event EventHandler<ThirdSeatInputRequest>? ThirdSeatInputRequired;

        /// <summary>研究タグが抽出されたとき発火</summary>
        public event EventHandler<ResearchTagsExtractedEventArgs>? ResearchTagsExtracted;

        /// <summary>
        /// HumanPriority: 人間の割り込み入力を要求するイベント。
        /// </summary>
        public event EventHandler<HumanPriorityInputRequest>? HumanPriorityInputRequired;

        public AutoDebateService(
            TransferService transferService,
            ApprovalQueue approvalQueue,
            AiSiteAdapterResolver adapterResolver,
            LogRepository logRepository)
        {
            _transferService = transferService;
            _approvalQueue = approvalQueue;
            _adapterResolver = adapterResolver;
            _logRepository = logRepository;
        }

        // ---------------------------------------------------------------
        // 制御
        // ---------------------------------------------------------------

        public void Start(AutoDebateConfig config)
        {
            if (IsRunning) return;
            _cts = new CancellationTokenSource();
            _isPaused = false;
            _pauseTcs = null;
            _humanInputTcs = null;
            _ = RunLoopAsync(config, _cts.Token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;
            _isPaused = false;
            _pauseTcs?.TrySetResult(true);
            _humanInputTcs?.TrySetResult(null);
            _humanInputTcs = null;
            NotifyStatus("討論を停止しました。");
            DebateStopped?.Invoke(this, EventArgs.Empty);
        }

        public void Pause()
        {
            if (!IsRunning || _isPaused) return;
            _isPaused = true;
            _pauseTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            NotifyStatus("一時停止中...");
        }

        public void Resume()
        {
            if (!_isPaused) return;
            _isPaused = false;
            _pauseTcs?.TrySetResult(true);
            _pauseTcs = null;
            NotifyStatus("再開しました。");
        }

        /// <summary>
        /// HumanPriority モード: 人間の割り込み発言を確定する。
        /// 空文字居はスキップ（AI ターン継続）。
        /// </summary>
        public void SubmitHumanPriorityInput(string? text)
        {
            _humanInputTcs?.TrySetResult(text);
            _humanInputTcs = null;
        }

        // ---------------------------------------------------------------
        // メインループ
        // ---------------------------------------------------------------

        private async Task RunLoopAsync(AutoDebateConfig config, CancellationToken ct)
        {
            NotifyStatus("自動討論を開始します。");
            int turn = 1;
            int phaseIndex = 0;
            DebateDirection direction = DebateDirection.LeftToRight;

            string leftSnapshot = string.Empty;
            string rightSnapshot = string.Empty;

            var turnRecords = new List<TransferRecord>();

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    // 一時停止
                    if (_isPaused)
                    {
                        NotifyStatus("一時停止中… 再開ボタンを押してください。");
                        await (_pauseTcs?.Task ?? Task.CompletedTask);
                        if (ct.IsCancellationRequested) break;
                    }

                    TurnAdvanced?.Invoke(this, turn);

                    // ---------------------------------------------------
                    // HumanPriority: ターン開始前に人間割り込みを確認
                    // ---------------------------------------------------
                    if (config.TurnPolicy == TurnPolicy.HumanPriority)
                    {
                        string? humanText = await WaitForHumanPriorityAsync(config, turnRecords, turn, ct);
                        if (ct.IsCancellationRequested) break;

                        if (!string.IsNullOrWhiteSpace(humanText))
                        {
                            // 人間発言を現在のターゲット側 AI に送信
                            var tgtWebView = direction == DebateDirection.LeftToRight ? config.RightWebView : config.LeftWebView;
                            string tgtUrl = direction == DebateDirection.LeftToRight ? config.RightUrl : config.LeftUrl;
                            var tgtAdapter = _adapterResolver.Resolve(tgtUrl);
                            if (tgtAdapter != null)
                            {
                                NotifyStatus($"[Turn {turn}] 人間発言を {tgtAdapter.SiteName} へ送信中…");
                                await tgtAdapter.SetInputAsync(tgtWebView, humanText);
                                await tgtAdapter.SendAsync(tgtWebView);

                                turnRecords.Add(new TransferRecord
                                {
                                    TurnNumber = turn,
                                    Direction = $"Human→{tgtAdapter.SiteName}",
                                    Text = humanText,
                                    Summary = _summaryService.Summarize(humanText)
                                });
                            }

                            // 方向を反転して次の AI ターンへ
                            direction = direction == DebateDirection.LeftToRight
                                ? DebateDirection.RightToLeft
                                : DebateDirection.LeftToRight;
                            phaseIndex = AdvancePhaseIndex(config.TurnPolicy, phaseIndex);
                            await Task.Delay(config.TurnIntervalMs, ct);
                            continue;
                        }
                        // humanText が空 → 通常 AI ターンへ fall-through
                    }

                    // ターンポリシーで方向を決定
                    direction = ResolveDirection(config.TurnPolicy, direction, turn, phaseIndex);

                    // 第3席を挿むか判定
                    bool isThirdTurn = ShouldInsertThirdSeat(config.ThirdSeat, turn);
                    if (isThirdTurn)
                    {
                        bool ok = await RunThirdSeatTurnAsync(config, turnRecords, ct);
                        if (!ok) break;
                        phaseIndex++;
                        continue;
                    }

                    // 通常ターン
                    bool isLeftTurn = direction == DebateDirection.LeftToRight;
                    var srcWebView = isLeftTurn ? config.LeftWebView : config.RightWebView;
                    var tgtWebView2 = isLeftTurn ? config.RightWebView : config.LeftWebView;
                    string srcUrl = isLeftTurn ? config.LeftUrl : config.RightUrl;
                    string tgtUrl2 = isLeftTurn ? config.RightUrl : config.LeftUrl;

                    bool isFinalTurn = IsFinalTurn(config, turn);

                    var srcAdapter = _adapterResolver.Resolve(srcUrl);
                    var tgtAdapter2 = _adapterResolver.Resolve(tgtUrl2);
                    if (srcAdapter == null || tgtAdapter2 == null)
                    { NotifyStatus("アダプタが見つかりません。停止します。"); break; }

                    // ---------------------------------------------------
                    // 生成指示の送信
                    // ★ 議題（topic）は Turn 1 のみ送信する
                    // ---------------------------------------------------
                    string topicText = config.Topic?.Trim() ?? string.Empty;
                    bool shouldSendTopic = !string.IsNullOrWhiteSpace(topicText) && turn == 1;

                    if (shouldSendTopic)
                    {
                        NotifyStatus($"ターン {turn}: {srcAdapter.SiteName} に議題を送信中...");

                        bool setOk = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            srcAdapter.SetInputAsync(srcWebView, topicText)
                        ).Task.Unwrap();

                        if (!setOk)
                        {
                            NotifyStatus($"ターン {turn}: {srcAdapter.SiteName} への入力設定に失敗しました。");
                            break;
                        }

                        bool sendOk = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            srcAdapter.SendAsync(srcWebView)
                        ).Task.Unwrap();

                        if (!sendOk)
                        {
                            NotifyStatus($"ターン {turn}: {srcAdapter.SiteName} への送信に失敗しました。");
                            break;
                        }
                        await Task.Delay(400, ct);
                    }
                    else if (string.IsNullOrWhiteSpace(topicText) && !isFinalTurn)
                    {
                        // 手動送信モード（または2ターン目以降で議題がない場合）
                        NotifyStatus($"ターン {turn}: {srcAdapter.SiteName} の生成完了を待機中...");
                    }
                    // else: topicあり Turn2以降 or 最終ターン → 何も送らず生成完了を待つだけ

                    string snapshot = isLeftTurn ? leftSnapshot : rightSnapshot;
                    string currentInputText = (turn == 1 && !string.IsNullOrWhiteSpace(topicText)) ? topicText : snapshot;
                    NotifyStatus($"ターン {turn} [{srcAdapter.SiteName}→{tgtAdapter2.SiteName}]: 生成完了を待機中...");

                    // 生成完了待機
                    string generatedText;
                    string? htmlSnapshotPath = null;
                    try
                    {
                        using var monitor = new ConversationMonitor(srcAdapter, srcWebView);
                        generatedText = await monitor.WaitForCompletionAsync(
                            snapshot, config.GenerationTimeoutMs, ct);
                        await Task.Delay(50, ct);

                        // ★ HTMLスナップショット キャプチャ (サイト別に最新ブロックを特定するスクリプトを使用)
                        string extractScript = srcAdapter.SiteName.Contains("Gemini", StringComparison.OrdinalIgnoreCase)
                            ? HtmlSnapshotStore.GeminiExtractScript
                            : srcAdapter.SiteName.Contains("Perplexity", StringComparison.OrdinalIgnoreCase)
                            ? HtmlSnapshotStore.PerplexityExtractScript
                            : HtmlSnapshotStore.DefaultExtractScript;

                        htmlSnapshotPath = await HtmlSnapshotStore.CaptureAsync(
                            srcWebView, $"Turn{turn}_{(isLeftTurn ? "Left" : "Right")}_{srcAdapter.SiteName}", extractScript);

                        string recheck = (await srcAdapter.ExtractLatestAsync(srcWebView))?.Trim() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(recheck)) generatedText = recheck;
                    }
                    catch (OperationCanceledException) { break; }

                    generatedText = generatedText?.Trim() ?? string.Empty;

                    // 前ターンの転送プレフィックス・なりきりブロックが混入していれば除去
                    generatedText = CleanGeneratedText(generatedText);

                    // ★ 追加: HTMLタグ・エンティティを純粋なテキストに変換
                    generatedText = StripHtml(generatedText);

                    if (string.IsNullOrWhiteSpace(generatedText) || generatedText == snapshot)
                    {
                        NotifyStatus($"ターン {turn}: 新規テキスト未検出。再試行します。");
                        await Task.Delay(800, ct);
                        turn--;
                        TurnAdvanced?.Invoke(this, turn);
                        continue;
                    }

                    if (isLeftTurn) leftSnapshot = generatedText;
                    else rightSnapshot = generatedText;

                    string tgtPrompt = isLeftTurn ? config.RightSystemPrompt : config.LeftSystemPrompt;

                    // ---------------------------------------------------
                    // 記録 (ログリーダに確実に残す)
                    // ★ ログリーダに必要な情報だけ記録（ターン番号・プロンプト設定・生成文章）
                    // ---------------------------------------------------
                    string summary = _summaryService.Summarize(generatedText);

                    // 転送先に付けるなりきりプロンプトの有無を備考として記録
                    string tgtPromptLabel = string.IsNullOrWhiteSpace(tgtPrompt)
                        ? "(なりきりなし)"
                        : $"なりきり: {(tgtPrompt.Length > 40 ? tgtPrompt[..40] + "…" : tgtPrompt)}";

                    var generatedRecord = new TransferRecord
                    {
                        TurnNumber = turn,
                        Direction = isLeftTurn ? "左生成" : "右生成",
                        Text = generatedText,
                        Timestamp = DateTime.Now,
                        SourceSite = srcAdapter.SiteName,
                        TargetSite = tgtAdapter2.SiteName,
                        Submitted = !isFinalTurn,   // 最終ターンは送信しないので false
                        Status = isFinalTurn ? "最終生成（送信なし）" : "生成→送信完了",
                        Summary = $"[T{turn} {(isLeftTurn ? "左" : "右")}] {tgtPromptLabel} | {summary}",
                        HtmlSnapshotPath = htmlSnapshotPath,
                        InputText = currentInputText
                    };

                    turnRecords.Add(generatedRecord);
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        config.LogRecords?.Insert(0, generatedRecord));

                    // Phase 5: 研究モード タグ抽出
                    if (config.ResearchMode)
                    {
                        var tags = _researchService.ExtractAndRegister(generatedText, turn);
                        if (tags.Count > 0)
                        {
                            ResearchTagsExtracted?.Invoke(this, new ResearchTagsExtractedEventArgs
                            {
                                TotalCount = _researchService.Entries.Count,
                                NewTags = tags
                            });
                        }
                    }

                    NotifyStatus($"ターン {turn}: 生成完了 [{summary}]");

                    // ---------------------------------------------------
                    // 転送テキスト組み立て
                    // ★ なりきりプロンプトはここで「転送先」のものを付ける
                    //   転送先のなりきりプロンプト = tgtPrompt
                    // ---------------------------------------------------

                    // （削除）最終成案指示は src 側への生成指示送信時に付加するため、ここでは不要

                    string roleLabel = GetRoleLabel(config.TurnPolicy, phaseIndex, isLeftTurn);
                    string prefix = $"[Turn {turn} {srcAdapter.SiteName}→{tgtAdapter2.SiteName}{roleLabel}]\n";

                    // ★ なりきりプロンプトが設定されている場合は転送テキストの先頭に付加
                    string promptHeader = string.IsNullOrWhiteSpace(tgtPrompt)
                        ? string.Empty
                        : "【なりきり設定】あなたは以下の人物像になりきって応答してください。\n" +
                          $"役割名: {tgtPrompt.Split('\n')[0]}\n" +
                          $"人物像:\n{tgtPrompt}\n\n" +
                          "口調・視点・知識レベル・価値観・関心において、この人物像を反映してください。" +
                          "ただし議題から逸れず、応答内容として自然な文章で出力してください。\n\n" +
                          "---\n以下は相手の発言です。上記の人物像になりきって返答してください。\n\n";

                    string bridge = config.AppendBridge
                        ? "\n\nこの意見についてどう考えますか？"
                        : string.Empty;

                    // 次ターンが最終ターンのとき、転送テキストに最終ターン指示を含める（1回だけ）
                    int nextTurn = turn + 1;
                    string finalInstruction = IsFinalTurn(config, nextTurn)
                        ? "\n\n【最終ターン指示】この応答では、ここまでの議論全体を踏まえて、" +
                          "議題に対する最終成案を必ず書き出してください。" +
                          "結論、採用案、理由、実行ステップまたは判断基準を整理して提示してください。"
                        : string.Empty;

                    // ★ 追加: generatedText に既に 【最終ターン指示】 が含まれている場合は二重付加しない
                    if (!string.IsNullOrEmpty(finalInstruction) &&
                        generatedText.Contains("【最終ターン指示】", StringComparison.Ordinal))
                    {
                        finalInstruction = string.Empty;
                    }

                    string transferText = $"{prefix}{promptHeader}{generatedText}{bridge}{finalInstruction}";

                    // ★ デバッグ追加: transferText の内容をログ出力
                    {
                        string dbgPreview = transferText.Length <= 500
                            ? transferText
                            : transferText[..300] + $"\n...(中略 {transferText.Length - 400}文字)...\n" + transferText[^100..];
                        NotifyStatus($"[DEBUG T{turn}] transferText.len={transferText.Length}\n{dbgPreview}");
                    }


                    if (isFinalTurn)
                    {
                        // ★ 最終ターンは転送不要。生成して記録したら終了
                        NotifyStatus($"ターン {turn}: 最終生成完了。転送せず終了します。");
                    }
                    else
                    {
                        // 承認待機
                        if (config.RequireApproval)
                        {
                            NotifyStatus($"ターン {turn}: 承認待ち...");
                            try
                            {
                                var result = await _approvalQueue.EnqueueAsync(
                                    srcAdapter.SiteName, tgtAdapter2.SiteName, transferText, true, ct);
                                if (!result.Approved) { NotifyStatus($"ターン {turn}: 却下。停止します。"); break; }
                                transferText = result.Text;
                            }
                            catch (OperationCanceledException) { break; }
                        }

                        // ★ 修正後: 転送失敗時のみリトライ。生成開始"確認"は補助情報に下げる
                        NotifyStatus($"ターン {turn}: {tgtAdapter2.SiteName} へ送信中...");

                        TransferResult transferResult = TransferResult.Fail("初期値");
                        bool transferOk = false;

                        // 送信前スナップショットを取る
                        string targetBeforeSnapshot = string.Empty;
                        try
                        {
                            targetBeforeSnapshot = await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                                async () => (await tgtAdapter2.ExtractLatestAsync(tgtWebView2))?.Trim() ?? string.Empty
                            ).Task.Unwrap();
                        }
                        catch
                        {
                            targetBeforeSnapshot = string.Empty;
                        }

                        for (int retry = 0; retry < 3; retry++)
                        {
                            if (retry > 0)
                            {
                                NotifyStatus($"ターン {turn}: {tgtAdapter2.SiteName} へ再送信中... ({retry + 1}/3)");
                                await Task.Delay(2000, ct);
                            }

                            transferResult = await _transferService.TransferAsync(
                                srcWebView, tgtWebView2, srcUrl, tgtUrl2,
                                submit: true, appendBridge: false, manualText: transferText);

                            if (!transferResult.Success)
                            {
                                NotifyStatus($"ターン {turn}: 送信失敗({retry + 1}/3) → {transferResult.Message}");
                                continue;
                            }

                            // ★ 送信成功後の確認は「必須条件」ではなく補助確認
                            bool generationDetected = false;
                            bool textAdvanced = false;

                            for (int chk = 0; chk < 10; chk++)
                            {
                                await Task.Delay(500, ct);

                                bool isGen = false;
                                string targetNow = string.Empty;
                                try
                                {
                                    isGen = await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                                        async () => await tgtAdapter2.IsGeneratingAsync(tgtWebView2)
                                    ).Task.Unwrap();
                                }
                                catch { }

                                try
                                {
                                    targetNow = await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                                        async () => (await tgtAdapter2.ExtractLatestAsync(tgtWebView2))?.Trim() ?? string.Empty
                                    ).Task.Unwrap();
                                }
                                catch { }

                                if (isGen)
                                {
                                    generationDetected = true;
                                    break;
                                }

                                if (!string.IsNullOrWhiteSpace(targetNow) &&
                                    targetNow != targetBeforeSnapshot &&
                                    targetNow.Length >= Math.Max(targetBeforeSnapshot.Length + 10, 30))
                                {
                                    textAdvanced = true;
                                    break;
                                }
                            }

                            // ★ 送信成功なら原則OK
                            transferOk = true;

                            if (generationDetected)
                            {
                                NotifyStatus($"ターン {turn}: {tgtAdapter2.SiteName} 生成開始確認。");
                            }
                            else if (textAdvanced)
                            {
                                NotifyStatus($"ターン {turn}: {tgtAdapter2.SiteName} テキスト更新確認。");
                            }
                            else
                            {
                                // ここで失敗扱いにしないのが重要
                                NotifyStatus($"ターン {turn}: {tgtAdapter2.SiteName} 送信完了（生成開始は未確認、次ターン監視へ移行）。");
                            }

                            break;
                        }

                        if (!transferOk)
                        {
                            NotifyStatus($"ターン {turn}: {tgtAdapter2.SiteName} への送信が3回失敗。停止します。");
                            break;
                        }

                        NotifyStatus($"ターン {turn}: 送信完了。");
                    }

                    phaseIndex = AdvancePhaseIndex(config.TurnPolicy, phaseIndex);

                    // 方向反転（RoundRobin / HumanPriority のみ。他はResolveDirectionが計算）
                    if (config.TurnPolicy == TurnPolicy.RoundRobin
                     || config.TurnPolicy == TurnPolicy.HumanPriority
                     || config.TurnPolicy == TurnPolicy.ModeratorSelect)
                    {
                        direction = direction == DebateDirection.LeftToRight
                            ? DebateDirection.RightToLeft
                            : DebateDirection.LeftToRight;
                    }

                    // ★ 最終ターン到達 → 記録済み・送信スキップ済みなのでそのまま終了
                    if (config.MaxTurns > 0 && turn >= config.MaxTurns)
                    {
                        NotifyStatus($"最大ターン {config.MaxTurns} に到達。全ターン記録完了。");
                        break;
                    }

                    turn++;
                    await Task.Delay(config.TurnIntervalMs, ct);
                }
            }
            finally
            {
                _cts = null;
                _isPaused = false;
                _pauseTcs = null;
                _humanInputTcs = null;
                DebateStopped?.Invoke(this, EventArgs.Empty);
                NotifyStatus("自動討論終了。");
            }
        }

        // ---------------------------------------------------------------
        // HumanPriority 割り込み待機
        // ---------------------------------------------------------------

        private async Task<string?> WaitForHumanPriorityAsync(
            AutoDebateConfig config,
            List<TransferRecord> records,
            int turn,
            CancellationToken ct)
        {
            _humanInputTcs = new TaskCompletionSource<string?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            string context = _summaryService.BuildModeratorSummary(records);
            HumanPriorityInputRequired?.Invoke(this, new HumanPriorityInputRequest
            {
                Summary = context,
                DisplayName = "HumanPriority",
                TimeoutMs = config.HumanPriorityTimeoutMs,
                OnInputReady = text => SubmitHumanPriorityInput(text)
            });

            NotifyStatus($"[Turn {turn}] 人間割り込み待機 ({config.HumanPriorityTimeoutMs / 1000}秒でスキップ)…");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(config.HumanPriorityTimeoutMs);

            try
            {
                return await _humanInputTcs.Task.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                _humanInputTcs = null;
                if (ct.IsCancellationRequested) return null;
                NotifyStatus($"[Turn {turn}] 人間割り込みなし → AI ターンへ");
                return null;
            }
        }

        // ---------------------------------------------------------------
        // 第3席ターン
        // ---------------------------------------------------------------

        private async Task<bool> RunThirdSeatTurnAsync(
            AutoDebateConfig config,
            List<TransferRecord> records,
            CancellationToken ct)
        {
            var third = config.ThirdSeat;
            if (third.Mode == ThirdSeatMode.Disabled) return true;

            NotifyStatus($"第3席 [{third.DisplayName}] ターン");

            string thirdText;

            if (third.Mode == ThirdSeatMode.Human)
            {
                string summary = _summaryService.BuildModeratorSummary(records);
                var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                ThirdSeatInputRequired?.Invoke(this, new ThirdSeatInputRequest
                {
                    Summary = summary,
                    Role = third.Role,
                    DisplayName = third.DisplayName,
                    OnInputReady = text => tcs.TrySetResult(text ?? string.Empty)
                });
                try { thirdText = await tcs.Task.WaitAsync(ct); }
                catch (OperationCanceledException) { return false; }

                if (string.IsNullOrWhiteSpace(thirdText)) return true;
            }
            else if (third.Mode == ThirdSeatMode.AiSite && third.WebView != null)
            {
                string summary = _summaryService.BuildModeratorSummary(records);
                var thirdAdapter = _adapterResolver.Resolve(third.Url);
                if (thirdAdapter == null) return true;

                string prompt = string.IsNullOrWhiteSpace(third.SystemPrompt)
                    ? summary
                    : $"{third.SystemPrompt}\n\n{summary}";

                await thirdAdapter.SetInputAsync(third.WebView, prompt);
                await thirdAdapter.SendAsync(third.WebView);

                using var monitor = new ConversationMonitor(thirdAdapter, third.WebView);
                try
                {
                    thirdText = await monitor.WaitForCompletionAsync(
                        string.Empty, config.GenerationTimeoutMs, ct);
                }
                catch (OperationCanceledException) { return false; }
            }
            else if (!string.IsNullOrWhiteSpace(third.StaticText))
            {
                thirdText = third.StaticText;
            }
            else return true;

            NotifyStatus($"第3席 [{third.DisplayName}]: {_summaryService.Summarize(thirdText)}");
            return true;
        }

        // ---------------------------------------------------------------
        // ポリシーヘルパー
        // ---------------------------------------------------------------

        private static DebateDirection ResolveDirection(
            TurnPolicy policy, DebateDirection current, int turn, int phase)
        {
            return policy switch
            {
                // RoundRobin / HumanPriority / ModeratorSelect:
                // ループ本体末尾で反転済みの current をそのまま使う
                TurnPolicy.RoundRobin => current,
                TurnPolicy.HumanPriority => current,
                TurnPolicy.ModeratorSelect => current,

                // CritiqueThenRefine: 提案(0)→批判(1)→改善(2)→提案(0)...
                //   phase偶数 = 左→右（提案・改善）
                //   phase奇数 = 右→左（批判）
                TurnPolicy.CritiqueThenRefine =>
                    phase % 2 == 0
                        ? DebateDirection.LeftToRight
                        : DebateDirection.RightToLeft,

                // ResearchReviewLoop: 仮説(0)→証明案(1)→反例(2)→査読(3)→仮説(0)...
                //   phase偶数 = 左→右（仮説・反例）
                //   phase奇数 = 右→左（証明案・査読）
                TurnPolicy.ResearchReviewLoop =>
                    phase % 2 == 0
                        ? DebateDirection.LeftToRight
                        : DebateDirection.RightToLeft,

                _ => current
            };
        }

        private static int AdvancePhaseIndex(TurnPolicy policy, int current) => policy switch
        {
            TurnPolicy.CritiqueThenRefine => (current + 1) % 3,
            TurnPolicy.ResearchReviewLoop => (current + 1) % 4,
            _ => current
        };

        private static string GetRoleLabel(TurnPolicy policy, int phase, bool isLeft)
        {
            if (policy == TurnPolicy.CritiqueThenRefine)
                return (phase % 3) switch { 0 => " 提案", 1 => " 批判", 2 => " 改善", _ => "" };
            if (policy == TurnPolicy.ResearchReviewLoop)
                return (phase % 4) switch { 0 => " 仮説", 1 => " 証明案", 2 => " 反例", 3 => " 査読", _ => "" };
            if (policy == TurnPolicy.HumanPriority)
                return " AI";
            return string.Empty;
        }

        private static bool IsFinalTurn(AutoDebateConfig config, int turn)
            => config.MaxTurns > 0 && turn == config.MaxTurns;

        private static bool ShouldInsertThirdSeat(ThirdSeatConfig third, int turn)
        {
            if (third.Mode == ThirdSeatMode.Disabled) return false;
            if (third.IntervalTurns <= 0) return false;

            int zeroBasedTurn = turn - 1;
            if (zeroBasedTurn <= 0) return false;

            return zeroBasedTurn % third.IntervalTurns == 0;
        }

        /// <summary>
        /// 生成テキストに前ターンの転送プレフィックスやなりきりブロックが
        /// 混入している場合、それらを除去して本文だけを返す。
        /// </summary>
        private static string CleanGeneratedText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            // ① [Turn N XxxSite→YyySite...] プレフィックス行を全て除去（本文中の引用も含む）
            var turnPrefixPattern = new System.Text.RegularExpressions.Regex(
                @"\[Turn\s+\d+[^\]]*\]\s*\r?\n?",
                System.Text.RegularExpressions.RegexOptions.Multiline);

            string cleaned = turnPrefixPattern.Replace(text, string.Empty);
            cleaned = cleaned.TrimStart('\r', '\n');

            // ② 【なりきり設定】ブロックを除去
            const string roleplayStart = "【なりきり設定】";
            const string roleplayEnd = "以下は相手の発言です。上記の人物像になりきって返答してください。";
            int rsIdx = cleaned.IndexOf(roleplayStart, StringComparison.Ordinal);
            if (rsIdx >= 0)
            {
                int reIdx = cleaned.IndexOf(roleplayEnd, rsIdx, StringComparison.Ordinal);
                if (reIdx >= 0)
                {
                    // roleplayEnd の末尾 + 改行2つまで除去
                    int endPos = reIdx + roleplayEnd.Length;
                    while (endPos < cleaned.Length && (cleaned[endPos] == '\r' || cleaned[endPos] == '\n'))
                        endPos++;
                    cleaned = cleaned[..rsIdx] + cleaned[endPos..];
                }
                else
                {
                    // --- 区切りがなければなりきりブロック以降を「次の---区切り」まで除去
                    int dashIdx = cleaned.IndexOf("---", rsIdx, StringComparison.Ordinal);
                    if (dashIdx >= 0)
                    {
                        int afterDash = dashIdx + 3;
                        while (afterDash < cleaned.Length && (cleaned[afterDash] == '\r' || cleaned[afterDash] == '\n'))
                            afterDash++;
                        cleaned = cleaned[..rsIdx] + cleaned[afterDash..];
                    }
                }
                cleaned = cleaned.Trim();
            }

            // ③ 【最終ターン指示】ブロックを除去（複数回出現・本文途中引用にも対応）
            const string finalStart = "【最終ターン指示】";
            const string finalEnd = "結論、採用案、理由、実行ステップまたは判断基準を整理して提示してください。";
            while (true)
            {
                int fiIdx = cleaned.IndexOf(finalStart, StringComparison.Ordinal);
                if (fiIdx < 0) break;

                int feIdx = cleaned.IndexOf(finalEnd, fiIdx, StringComparison.Ordinal);
                if (feIdx >= 0)
                {
                    // 本文途中に引用として埋まっている → 前後をつなぐ
                    int endPos = feIdx + finalEnd.Length;
                    while (endPos < cleaned.Length && (cleaned[endPos] == '\r' || cleaned[endPos] == '\n'))
                        endPos++;
                    cleaned = cleaned[..fiIdx] + cleaned[endPos..];
                }
                else
                {
                    // finalEnd が見つからない → 以降を末尾まで全カット
                    cleaned = cleaned[..fiIdx].TrimEnd();
                    break;
                }
            }

            return string.IsNullOrWhiteSpace(cleaned) ? text : cleaned.Trim();
        }

        /// <summary>
        /// HTMLタグを除去して純粋なテキストにする。
        /// &amp; &lt; &gt; &nbsp; 等のHTMLエンティティもデコードする。
        /// </summary>
        private static string StripHtml(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            // <br> / <br/> → 改行に変換
            text = System.Text.RegularExpressions.Regex.Replace(
                text, @"<br\s*/?>", "\n",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // </p> → 改行に変換
            text = System.Text.RegularExpressions.Regex.Replace(
                text, @"</p\s*>", "\n",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // 残りの全HTMLタグを除去
            text = System.Text.RegularExpressions.Regex.Replace(
                text, @"<[^>]+>", string.Empty);

            // HTMLエンティティをデコード（&amp; → & 等）
            text = System.Net.WebUtility.HtmlDecode(text);

            // 3行以上連続する空行を2行に圧縮
            text = System.Text.RegularExpressions.Regex.Replace(
                text, @"\n{3,}", "\n\n");

            return text.Trim();
        }

        private void NotifyStatus(string msg) => StatusChanged?.Invoke(this, msg);
    }

    /// <summary>第3席 Human モードの入力リクエスト</summary>
    public sealed class ThirdSeatInputRequest
    {
        public string Summary { get; init; } = string.Empty;
        public DebateRole Role { get; init; }
        public string DisplayName { get; init; } = string.Empty;
        public Action<string?>? OnInputReady { get; init; }
    }

    /// <summary>HumanPriority モードの入力リクエスト</summary>
    public sealed class HumanPriorityInputRequest
    {
        public string Summary { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public int TimeoutMs { get; init; }
        public Action<string?>? OnInputReady { get; init; }
    }

    /// <summary>研究タグ抽出イベント引数</summary>
    public sealed class ResearchTagsExtractedEventArgs : EventArgs
    {
        public int TotalCount { get; init; }
        public List<ResearchTagEntry> NewTags { get; init; } = new();
    }
}
