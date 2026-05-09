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
        public SummaryService      SummaryService  => _summaryService;

        public event EventHandler<string>? StatusChanged;
        public event EventHandler<int>?    TurnAdvanced;
        public event EventHandler?         DebateStopped;
        /// <summary>第3席への入力が必要なとき発火（Human モード）</summary>
        public event EventHandler<ThirdSeatInputRequest>? ThirdSeatInputRequired;
        /// <summary>研究タグが抽出されたとき発火</summary>
        public event EventHandler<List<ResearchTagEntry>>? ResearchTagsExtracted;

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
            int turn = 0;

            // CritiqueThenRefine: Proposer → Critic → Refiner (左→右→左)
            // ResearchReviewLoop: Hypothesis → Proof → Counter → Review
            int phaseIndex = 0;
            DebateDirection direction = DebateDirection.LeftToRight;

            string leftSnapshot  = string.Empty;
            string rightSnapshot = string.Empty;
            string thirdSnapshot = string.Empty;

            // 各ターンの TransferRecord 一覧（司会サマリー用）
            var turnRecords = new List<TransferRecord>();

            // ★ 追加: 連続失敗カウンター
            int consecutiveFailCount = 0;
            const int MaxConsecutiveFail = 3;

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

                    turn++;
                    TurnAdvanced?.Invoke(this, turn);

                    // ターンポリシーで方向を決定
                    direction = ResolveDirection(config.TurnPolicy, direction, turn, phaseIndex);

                    // 第3席を挟むか判定
                    bool isThirdTurn = ShouldInsertThirdSeat(config.ThirdSeat, turn);
                    if (isThirdTurn)
                    {
                        bool ok = await RunThirdSeatTurnAsync(config, turnRecords, turn, ct);
                        if (!ok) break;
                        phaseIndex++;
                        consecutiveFailCount = 0;
                        continue;
                    }

                    // 通常ターン
                    bool isLeftTurn = direction == DebateDirection.LeftToRight;
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

                    consecutiveFailCount = 0; // ★ 成功したらリセット

                    if (isLeftTurn) leftSnapshot  = generatedText;
                    else            rightSnapshot = generatedText;

                    // Phase 5: 研究モード タグ抽出
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

                    // 1行要約
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

                    // 承認待機（ApprovalPolicy による動的判定）
                    bool needsApproval = config.RequireApproval;
                    if (config.ApprovalPolicy != null)
                    {
                        // 直前ターンに引用があったかを判定（新しい rec はまだ未生成）
                        bool hasQuote = turnRecords.Count > 0 &&
                                        (turnRecords[turnRecords.Count - 1].QuotedMessageIds?.Count ?? 0) > 0;
                        needsApproval = config.ApprovalPolicy.ShouldRequireApproval(
                            generatedText,
                            hasQuote:        hasQuote,
                            isAfterRecovery: false);
                    }

                    if (needsApproval)
                    {
                        NotifyStatus($"\u30bf\u30fc\u30f3 {turn}: \u627f\u8a8d\u5f85\u3061...");
                        try
                        {
                            var result = await _approvalQueue.EnqueueAsync(
                                srcAdapter.SiteName, tgtAdapter.SiteName, transferText, true, ct);
                            if (!result.Approved) { NotifyStatus($"\u30bf\u30fc\u30f3 {turn}: \u5374\u4e0b\u3002\u505c\u6b62\u3057\u307e\u3059\u3002"); break; }
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
                        // ★ break → リトライに変更（連続失敗のみ停止）
                        NotifyStatus($"ターン {turn}: 送信失敗 → {transferResult.Message}  (2秒後リトライ)");
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

                    consecutiveFailCount = 0; // ★ 送信成功でリセット

                    // TransferRecord を司会サマリー用リストに追加
                    var rec = new TransferRecord
                    {
                        TurnNumber = turn,
                        Direction  = $"{srcAdapter.SiteName}\u2192{tgtAdapter.SiteName}",
                        Text       = generatedText,
                        Summary    = summary,
                        MessageId  = $"msg-{turn}-{(isLeftTurn ? "L" : "R")}"
                    };
                    turnRecords.Add(rec);
                    await _sessionRepository.AppendAsync(rec);

                    NotifyStatus($"ターン {turn}: 送信完了。");

                    // ポリシーフェーズ進行
                    phaseIndex = AdvancePhaseIndex(config.TurnPolicy, phaseIndex);

                    // 方向反転（RoundRobin）
                    if (config.TurnPolicy == TurnPolicy.RoundRobin)
                        direction = direction == DebateDirection.LeftToRight
                            ? DebateDirection.RightToLeft
                            : DebateDirection.LeftToRight;

                    if (config.MaxTurns > 0 && turn >= config.MaxTurns)
                    {
                        NotifyStatus($"最大ターン数 {config.MaxTurns} に到達。討論終了。");
                        break;
                    }

                    NotifyStatus($"\u30bf\u30fc\u30f3 {turn}: \u6b21\u306e\u30bf\u30fc\u30f3\u307e\u3067 {config.PostSendWaitMs / 1000} \u79d2\u5f85\u6a5f...");
                    await Task.Delay(config.PostSendWaitMs, ct);
                }
            }
            finally
            {
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
                // Human モード: UI に入力を求める
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

                if (string.IsNullOrWhiteSpace(thirdText)) return true; // スキップ
            }
            else if (third.Mode == ThirdSeatMode.AiSite && third.WebView != null)
            {
                // AI サイトモード: 司会サマリーを AI に投げる
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
                TurnPolicy.RoundRobin => current, // RunLoop 側で反転
                TurnPolicy.CritiqueThenRefine =>
                    // Phase 0=Proposer(Left), 1=Critic(Right), 2=Refiner(Left)
                    phase % 3 == 1 ? DebateDirection.RightToLeft : DebateDirection.LeftToRight,
                TurnPolicy.ResearchReviewLoop =>
                    // Phase 0=Hypothesis(Left), 1=Proof(Left), 2=Counter(Right), 3=Review(Right)
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
            {
                return (phase % 3) switch
                {
                    0 => " \u63d0\u6848",
                    1 => " \u6279\u5224",
                    2 => " \u6539\u5584",
                    _ => ""
                };
            }
            if (policy == TurnPolicy.ResearchReviewLoop)
            {
                return (phase % 4) switch
                {
                    0 => " \u4eee\u8aac",
                    1 => " \u8a3c\u660e\u6848",
                    2 => " \u53cd\u4f8b",
                    3 => " \u67fb\u8aad",
                    _ => ""
                };
            }
            return string.Empty;
        }

        private static bool ShouldInsertThirdSeat(ThirdSeatConfig third, int turn)
        {
            if (third.Mode == ThirdSeatMode.Disabled) return false;
            if (third.IntervalTurns <= 0) return false;
            return turn % third.IntervalTurns == 0;
        }

        private void NotifyStatus(string msg) => StatusChanged?.Invoke(this, msg);
    }

}
