using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace gironWin
{
    public enum DebateDirection { LeftToRight, RightToLeft, ThirdToLeft, ThirdToRight }

    /// <summary>
    /// Phase 3-5: 第3席・TurnPolicy・ResearchMode 対応 AutoDebateService
    /// MaxTurns = 左右それぞれの発言回数（左N回・右N回で終了）
    /// </summary>
    public sealed class AutoDebateService
    {
        private readonly TransferService       _transferService;
        private readonly ApprovalQueue         _approvalQueue;
        private readonly AiSiteAdapterResolver _adapterResolver;
        private readonly SummaryService        _summaryService  = new();
        private readonly ResearchService       _researchService = new();
        private readonly LogRepository         _logRepository;
        private readonly SessionRepository     _sessionRepository;

        private CancellationTokenSource? _cts;
        private bool _isPaused;
        private TaskCompletionSource<bool>? _pauseTcs;

        public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;
        public bool IsPaused  => _isPaused;

        public ResearchService ResearchService => _researchService;
        public SummaryService  SummaryService  => _summaryService;

        public event EventHandler<string>? StatusChanged;
        /// <summary>左右各カウントの小さい方（進捗表示用）を通知する</summary>
        public event EventHandler<int>?    TurnAdvanced;
        public event EventHandler?         DebateStopped;
        public event EventHandler<ThirdSeatInputRequest>? ThirdSeatInputRequired;
        public event EventHandler<List<ResearchTagEntry>>? ResearchTagsExtracted;
        /// <summary>デバッグログ出力先（DebugLogWindow が購読する）</summary>
        public event EventHandler<string>? DebugLogEmitted;

        public AutoDebateService(
            TransferService transferService,
            ApprovalQueue approvalQueue,
            AiSiteAdapterResolver adapterResolver,
            LogRepository logRepository,
            SessionRepository sessionRepository)
        {
            _transferService    = transferService;
            _approvalQueue      = approvalQueue;
            _adapterResolver    = adapterResolver;
            _logRepository      = logRepository;
            _sessionRepository  = sessionRepository;
        }

        // ---------------------------------------------------------------
        // 制御
        // ---------------------------------------------------------------

        public void Start(AutoDebateConfig config)
        {
            if (IsRunning) return;
            _cts      = new CancellationTokenSource();
            _isPaused = false;
            _pauseTcs = null;
            _ = RunLoopAsync(config, _cts.Token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts      = null;
            _isPaused = false;
            _pauseTcs?.TrySetResult(true);
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

        // ---------------------------------------------------------------
        // メインループ
        // ---------------------------------------------------------------

        private async Task RunLoopAsync(AutoDebateConfig config, CancellationToken ct)
        {
            NotifyStatus("自動討論を開始します。");

            // turn       = 送信総回数（左右・第3席 全部合計）
            // leftCount  = 左席の発言回数（左→右 完了で +1）
            // rightCount = 右席の発言回数（右→左 完了で +1）
            // 終了条件: leftCount >= MaxTurns && rightCount >= MaxTurns
            int turn       = 0;
            int leftCount  = 0;
            int rightCount = 0;

            int phaseIndex = 0;
            DebateDirection direction = DebateDirection.LeftToRight;

            string leftSnapshot  = string.Empty;
            string rightSnapshot = string.Empty;

            var turnRecords = new List<TransferRecord>();

            int consecutiveFailCount = 0;
            const int MaxConsecutiveFail = 3;

            DebugLog($"[RunLoop] 開始 MaxTurns={config.MaxTurns} TurnPolicy={config.TurnPolicy} (左右それぞれ{config.MaxTurns}回発言で終了)");

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (_isPaused)
                    {
                        NotifyStatus("一時停止中… 再開ボタンを押してください。");
                        await (_pauseTcs?.Task ?? Task.CompletedTask);
                        if (ct.IsCancellationRequested) break;
                    }

                    turn++;

                    // 方向を決定
                    direction = ResolveDirection(config.TurnPolicy, direction, turn, phaseIndex);
                    bool isLeftTurn = direction == DebateDirection.LeftToRight;

                    // 第3席を挑むか判定（第3席は leftCount/rightCount に影響しない）
                    bool isThirdTurn = ShouldInsertThirdSeat(config.ThirdSeat, turn);
                    if (isThirdTurn)
                    {
                        DebugLog($"[Turn {turn}] 第3席ターン (leftCount={leftCount} rightCount={rightCount} 変化なし)");
                        bool ok = await RunThirdSeatTurnAsync(config, turnRecords, turn, ct);
                        if (!ok) break;
                        phaseIndex++;
                        consecutiveFailCount = 0;
                        continue;
                    }

                    DebugLog($"[Turn {turn}] 開始 direction={direction} isLeftTurn={isLeftTurn} leftCount={leftCount} rightCount={rightCount} phaseIndex={phaseIndex}");

                    // 通常ターン
                    var srcWebView  = isLeftTurn ? config.LeftWebView  : config.RightWebView;
                    var tgtWebView  = isLeftTurn ? config.RightWebView : config.LeftWebView;
                    string srcUrl   = isLeftTurn ? config.LeftUrl      : config.RightUrl;
                    string tgtUrl   = isLeftTurn ? config.RightUrl     : config.LeftUrl;
                    string tgtPrompt = isLeftTurn ? config.RightSystemPrompt : config.LeftSystemPrompt;

                    var srcAdapter = _adapterResolver.Resolve(srcUrl);
                    var tgtAdapter = _adapterResolver.Resolve(tgtUrl);
                    if (srcAdapter == null || tgtAdapter == null)
                    { NotifyStatus("アダプタが見つかりません。停止します。"); break; }

                    string snapshot = isLeftTurn ? leftSnapshot : rightSnapshot;
                    NotifyStatus($"ターン {turn} [{srcAdapter.SiteName}→{tgtAdapter.SiteName}]: 生成完了を待機中...");

                    // 生成完了待機
                    string generatedText;
                    try
                    {
                        using var monitor = new ConversationMonitor(srcAdapter, srcWebView);
                        generatedText = await monitor.WaitForCompletionAsync(
                            snapshot, config.GenerationTimeoutMs, ct);
                        await Task.Delay(50, ct);
                        string recheck = (await srcAdapter.ExtractLatestAsync(srcWebView))?.Trim() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(recheck)) generatedText = recheck;
                    }
                    catch (OperationCanceledException) { break; }

                    generatedText = generatedText?.Trim() ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(generatedText) || generatedText == snapshot)
                    {
                        NotifyStatus($"ターン {turn}: 新規テキスト未検出。再試行します。");
                        DebugLog($"[Turn {turn}] テキスト未検出 → turn-- consecutiveFail={consecutiveFailCount + 1}");
                        await Task.Delay(800, ct);
                        turn--;
                        consecutiveFailCount++;
                        if (consecutiveFailCount >= MaxConsecutiveFail)
                        {
                            NotifyStatus($"連続 {MaxConsecutiveFail} 回テキスト未検出。討論を停止します。");
                            break;
                        }
                        continue;
                    }

                    consecutiveFailCount = 0;

                    if (isLeftTurn) leftSnapshot  = generatedText;
                    else            rightSnapshot = generatedText;

                    // 研究モード タグ抽出
                    if (config.ResearchMode)
                    {
                        string msgId = $"msg-{turn}-{(isLeftTurn ? "L" : "R")}";
                        var tags = _researchService.ExtractAndAdd(generatedText, turn, msgId);
                        if (tags.Count > 0)
                        {
                            ResearchTagsExtracted?.Invoke(this, tags);
                            foreach (var tag in tags)
                                await _sessionRepository.AppendResearchTagAsync(tag);
                        }
                    }

                    string summary = _summaryService.Summarize(generatedText);
                    NotifyStatus($"ターン {turn}: 生成完了 [{summary}]");

                    // 送信テキスト組み立て
                    string roleLabel = GetRoleLabel(config.TurnPolicy, phaseIndex, isLeftTurn);
                    string prefix = $"[Turn {turn} {srcAdapter.SiteName}→{tgtAdapter.SiteName}{roleLabel}]\n";
                    string body   = config.AppendBridge
                        ? $"{generatedText}\n\nこの意見についてどう考えますか？"
                        : generatedText;

                    string transferText = string.IsNullOrWhiteSpace(tgtPrompt)
                        ? $"{prefix}{body}"
                        : $"{tgtPrompt}\n\n{prefix}{body}";

                    // 承認判定
                    bool needsApproval = config.RequireApproval;
                    if (config.ApprovalPolicy != null)
                    {
                        bool hasQuote = turnRecords.Count > 0 &&
                                        (turnRecords[turnRecords.Count - 1].QuotedMessageIds?.Count ?? 0) > 0;
                        needsApproval = config.ApprovalPolicy.ShouldRequireApproval(
                            generatedText, hasQuote: hasQuote, isAfterRecovery: false);
                    }

                    if (needsApproval)
                    {
                        NotifyStatus($"ターン {turn}: 承認待ち...");
                        try
                        {
                            var result = await _approvalQueue.EnqueueAsync(
                                srcAdapter.SiteName, tgtAdapter.SiteName, transferText, true, ct);
                            if (!result.Approved) { NotifyStatus($"ターン {turn}: 却下。停止します。"); break; }
                            transferText = result.Text;
                        }
                        catch (OperationCanceledException) { break; }
                    }

                    NotifyStatus($"ターン {turn}: {tgtAdapter.SiteName} へ送信中...");
                    var transferResult = await _transferService.TransferAsync(
                        srcWebView, tgtWebView, srcUrl, tgtUrl,
                        submit: true, appendBridge: false, manualText: transferText);

                    if (!transferResult.Success)
                    {
                        NotifyStatus($"ターン {turn}: 送信失敗 → {transferResult.Message}  (2秒後リトライ)");
                        DebugLog($"[Turn {turn}] 送信失敗 → turn-- consecutiveFail={consecutiveFailCount + 1}");
                        await Task.Delay(2000, ct);
                        turn--;
                        consecutiveFailCount++;
                        if (consecutiveFailCount >= MaxConsecutiveFail)
                        {
                            NotifyStatus($"連続 {MaxConsecutiveFail} 回送信失敗。討論を停止します。");
                            break;
                        }
                        continue;
                    }

                    consecutiveFailCount = 0;

                    var rec = new TransferRecord
                    {
                        TurnNumber = turn,
                        Direction  = $"{srcAdapter.SiteName}→{tgtAdapter.SiteName}",
                        Text       = generatedText,
                        Summary    = summary,
                        MessageId  = $"msg-{turn}-{(isLeftTurn ? "L" : "R")}"
                    };
                    turnRecords.Add(rec);
                    await _sessionRepository.AppendAsync(rec);

                    // ★ 左右個別に発言回数をカウント
                    if (isLeftTurn)
                    {
                        leftCount++;
                        DebugLog($"[Turn {turn}] 左→右 完了 → leftCount={leftCount} / MaxTurns={config.MaxTurns}");
                    }
                    else
                    {
                        rightCount++;
                        DebugLog($"[Turn {turn}] 右→左 完了 → rightCount={rightCount} / MaxTurns={config.MaxTurns}");
                    }

                    // 進捗を UI に通知（小さい方のカウントを表示）
                    TurnAdvanced?.Invoke(this, Math.Min(leftCount, rightCount));

                    // ★ 左右両方が MaxTurns に達したら終了
                    if (config.MaxTurns > 0
                        && leftCount  >= config.MaxTurns
                        && rightCount >= config.MaxTurns)
                    {
                        NotifyStatus($"左右各 {config.MaxTurns} 回発言に到達。討論終了。");
                        DebugLog($"[RunLoop] MaxTurns 到達: leftCount={leftCount} rightCount={rightCount} >= MaxTurns={config.MaxTurns} → 終了");
                        break;
                    }

                    NotifyStatus($"ターン {turn}: 送信完了。");

                    // フェーズ進行
                    phaseIndex = AdvancePhaseIndex(config.TurnPolicy, phaseIndex);

                    // 方向反転（RoundRobin）
                    if (config.TurnPolicy == TurnPolicy.RoundRobin)
                        direction = direction == DebateDirection.LeftToRight
                            ? DebateDirection.RightToLeft
                            : DebateDirection.LeftToRight;

                    NotifyStatus($"次のターンまで {config.PostSendWaitMs / 1000} 秒待機...");
                    await Task.Delay(config.PostSendWaitMs, ct);
                }
            }
            finally
            {
                DebugLog($"[RunLoop] 終了 turn={turn} leftCount={leftCount} rightCount={rightCount} MaxTurns={config.MaxTurns}");
                _cts      = null;
                _isPaused = false;
                _pauseTcs = null;
                DebateStopped?.Invoke(this, EventArgs.Empty);
                NotifyStatus("自動討論終了。");
            }
        }

        // ---------------------------------------------------------------
        // 第3席ターン
        // ---------------------------------------------------------------

        private async Task<bool> RunThirdSeatTurnAsync(
            AutoDebateConfig config,
            List<TransferRecord> records,
            int turn,
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
                    TurnNumber = turn,
                    Context    = summary,
                    Role       = third.Role.ToString(),
                    OnSubmit   = text => tcs.TrySetResult(text ?? string.Empty)
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
                TurnPolicy.RoundRobin => current,
                TurnPolicy.CritiqueThenRefine =>
                    phase % 3 == 1 ? DebateDirection.RightToLeft : DebateDirection.LeftToRight,
                TurnPolicy.ResearchReviewLoop =>
                    phase % 4 >= 2 ? DebateDirection.RightToLeft : DebateDirection.LeftToRight,
                _ => current
            };
        }

        private static int AdvancePhaseIndex(TurnPolicy policy, int current) => policy switch
        {
            TurnPolicy.CritiqueThenRefine  => (current + 1) % 3,
            TurnPolicy.ResearchReviewLoop  => (current + 1) % 4,
            _ => current
        };

        private static string GetRoleLabel(TurnPolicy policy, int phase, bool isLeft)
        {
            if (policy == TurnPolicy.CritiqueThenRefine)
                return (phase % 3) switch { 0 => " 提案", 1 => " 批判", 2 => " 改善", _ => "" };
            if (policy == TurnPolicy.ResearchReviewLoop)
                return (phase % 4) switch { 0 => " 仮説", 1 => " 証明案", 2 => " 反例", 3 => " 査読", _ => "" };
            return string.Empty;
        }

        private static bool ShouldInsertThirdSeat(ThirdSeatConfig third, int turn)
        {
            if (third.Mode == ThirdSeatMode.Disabled) return false;
            if (third.IntervalTurns <= 0) return false;
            return turn % third.IntervalTurns == 0;
        }

        private void NotifyStatus(string msg) => StatusChanged?.Invoke(this, msg);

        /// <summary>デバッグログをイベントで発火する</summary>
        private void DebugLog(string msg)
            => DebugLogEmitted?.Invoke(this, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}");
    }
}
