using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace gironWin
{
    public sealed class AutoDebateService
    {
        private readonly TransferService _transferService;
        private readonly ApprovalQueue _approvalQueue;
        private readonly AiSiteAdapterResolver _adapterResolver;
        private readonly SummaryService _summaryService = new();
        private readonly ResearchModeService _researchService = new();
        private CancellationTokenSource? _cts;
        private TaskCompletionSource<bool>? _runCompletion;
        private TaskCompletionSource<bool>? _pauseTcs;
        private TaskCompletionSource<string?>? _humanInputTcs;
        private bool _isStopping, _isPaused;
        private TransferRecord? _activeRecord;
        public bool IsRunning => _runCompletion != null && !_runCompletion.Task.IsCompleted;
        public bool IsStopping => _isStopping;
        public bool IsPaused => _isPaused;
        public Task Completion => _runCompletion?.Task ?? Task.CompletedTask;
        public ResearchModeService ResearchService => _researchService;
        public SummaryService SummaryService => _summaryService;
        public event EventHandler<string>? StatusChanged;
        public event EventHandler<int>? TurnAdvanced;
        public event EventHandler? DebateStopped;
        public event EventHandler<ThirdSeatInputRequest>? ThirdSeatInputRequired;
        public event EventHandler<HumanPriorityInputRequest>? HumanPriorityInputRequired;
        public event EventHandler<ResearchTagsExtractedEventArgs>? ResearchTagsExtracted;

        public AutoDebateService(TransferService transferService, ApprovalQueue approvalQueue,
            AiSiteAdapterResolver adapterResolver, LogRepository logRepository)
        { _transferService = transferService; _approvalQueue = approvalQueue; _adapterResolver = adapterResolver; }

        public void Start(AutoDebateConfig config)
        {
            if (IsRunning) return;
            var cts = new CancellationTokenSource();
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _cts = cts; _runCompletion = completion;
            _isStopping = false; _isPaused = false; _pauseTcs = null; _humanInputTcs = null; _activeRecord = null;
            _ = ExecuteRunAsync(config, cts, completion);
        }
        public void Stop()
        {
            var cts = _cts;
            if (!IsRunning || cts == null || _isStopping) return;
            _isStopping = true;
            try { cts.Cancel(); }
            finally { _isPaused = false; _pauseTcs?.TrySetResult(true); _humanInputTcs?.TrySetResult(null); }
        }
        public Task StopAsync() { var completion = Completion; Stop(); return completion; }
        public void Pause()
        {
            if (!IsRunning || IsStopping || _isPaused) return;
            _pauseTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _isPaused = true; NotifyStatus("一時停止中...");
        }
        public void Resume()
        {
            if (!IsRunning || IsStopping || !_isPaused) return;
            _isPaused = false; _pauseTcs?.TrySetResult(true); _pauseTcs = null; NotifyStatus("再開しました。");
        }
        public void SubmitHumanPriorityInput(string? text) => _humanInputTcs?.TrySetResult(text);

        private async Task ExecuteRunAsync(AutoDebateConfig config, CancellationTokenSource cts,
            TaskCompletionSource<bool> completion)
        {
            string reason = "正常終了";
            try { reason = await RunLoopAsync(config, cts.Token); }
            catch (OperationCanceledException) when (cts.IsCancellationRequested) { reason = "手動停止"; }
            catch (Exception ex)
            {
                reason = ex is TimeoutException ? "生成待機タイムアウト: " + ex.Message : "エラー: " + ex.Message;
                try { AppLogger.Error(LogCategory.RunLoop, reason, ex); } catch { }
            }
            finally
            {
                if (_activeRecord != null && !_activeRecord.Submitted) _activeRecord.Status = reason;
                _activeRecord = null; _isPaused = false;
                _pauseTcs?.TrySetResult(true); _humanInputTcs?.TrySetResult(null);
                _pauseTcs = null; _humanInputTcs = null;
                if (ReferenceEquals(_cts, cts)) _cts = null;
                cts.Dispose();
                try { NotifyStatus("自動討論終了: " + reason); } catch { }
                try { DebateStopped?.Invoke(this, EventArgs.Empty); } catch { }
                _isStopping = false; completion.TrySetResult(true);
            }
        }

        private async Task<string> RunLoopAsync(AutoDebateConfig config, CancellationToken ct)
        {
            int turn = 1, phase = 0;
            bool left = true, topicSent = false;
            var records = new List<TransferRecord>();
            var pending = new Dictionary<WebView2, GenerationRequest>();
            var inputs = new Dictionary<WebView2, string>();
            var rawSnapshots = new Dictionary<WebView2, string>();
            var thirdHandled = new HashSet<int>();
            NotifyStatus("自動討論を開始します。");
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (_isPaused) await (_pauseTcs?.Task ?? Task.CompletedTask).WaitAsync(ct);
                ct.ThrowIfCancellationRequested();
                TurnAdvanced?.Invoke(this, turn);
                if (config.TurnPolicy == TurnPolicy.HumanPriority)
                {
                    string? human = await WaitForHumanPriorityAsync(config, records, turn, ct);
                    if (!string.IsNullOrWhiteSpace(human))
                    {
                        var hv = left ? config.RightWebView : config.LeftWebView;
                        var ha = RequireAdapter(left ? config.RightUrl : config.LeftUrl);
                        pending[hv] = await SendCheckedAsync(ha, hv, human, ct);
                        inputs[hv] = human;
                        records.Add(new TransferRecord { TurnNumber = turn, Direction = "Human→" + ha.SiteName,
                            Text = human, Summary = _summaryService.Summarize(human) });
                        left = !left; phase = AdvancePhaseIndex(config.TurnPolicy, phase);
                        await Task.Delay(Math.Max(0, config.TurnIntervalMs), ct);
                        continue;
                    }
                }
                if (ShouldInsertThirdSeat(config.ThirdSeat, turn) && thirdHandled.Add(turn))
                    await RunThirdSeatTurnAsync(config, records, ct);
                var src = left ? config.LeftWebView : config.RightWebView;
                var tgt = left ? config.RightWebView : config.LeftWebView;
                string srcUrl = left ? config.LeftUrl : config.RightUrl;
                string tgtUrl = left ? config.RightUrl : config.LeftUrl;
                var source = RequireAdapter(srcUrl);
                var target = RequireAdapter(tgtUrl);
                bool final = config.MaxTurns > 0 && turn >= config.MaxTurns;
                string topic = config.Topic?.Trim() ?? string.Empty;
                if (turn == 1 && !topicSent && topic.Length > 0)
                {
                    NotifyStatus($"ターン {turn}: {source.SiteName} に議題を送信中...");
                    pending[src] = await SendCheckedAsync(source, src, topic, ct);
                    inputs[src] = topic; topicSent = true;
                }
                NotifyStatus($"ターン {turn} [{source.SiteName}→{target.SiteName}]: 生成完了を待機中...");
                pending.TryGetValue(src, out var request);
                using var monitor = new ConversationMonitor(source, src);
                string raw = await monitor.WaitForCompletionAsync(rawSnapshots.GetValueOrDefault(src, string.Empty),
                    config.GenerationTimeoutMs, ct, request);
                pending.Remove(src); rawSnapshots[src] = raw;
                string text = StripHtml(CleanGeneratedText(raw));
                if (string.IsNullOrWhiteSpace(text))
                    throw new InvalidDataException($"ターン {turn}: 取得した回答の本文が空です。ターン番号は進めません。");
                string? html = null;
                try
                {
                    string script = source.SiteName.Contains("Gemini", StringComparison.OrdinalIgnoreCase)
                        ? HtmlSnapshotStore.GeminiExtractScript
                        : source.SiteName.Contains("Perplexity", StringComparison.OrdinalIgnoreCase)
                            ? HtmlSnapshotStore.PerplexityExtractScript : HtmlSnapshotStore.DefaultExtractScript;
                    html = await HtmlSnapshotStore.CaptureAsync(src, $"Turn{turn}_{(left ? "Left" : "Right")}_{source.SiteName}", script);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { AppLogger.Warn(LogCategory.RunLoop, "HTML保存失敗（本文は保持）: " + ex.Message); }
                ct.ThrowIfCancellationRequested();
                var record = new TransferRecord { TurnNumber = turn, Direction = left ? "左生成" : "右生成",
                    Text = text, Timestamp = DateTime.Now, SourceSite = source.SiteName, TargetSite = target.SiteName,
                    Submitted = false, Status = final ? "最終生成（送信なし）" : "生成完了・送信前",
                    Summary = _summaryService.Summarize(text), HtmlSnapshotPath = html,
                    InputText = inputs.GetValueOrDefault(src, string.Empty) };
                records.Add(record); _activeRecord = record;
                Application.Current.Dispatcher.Invoke(() => config.LogRecords?.Insert(0, record));
                if (config.ResearchMode)
                {
                    var tags = _researchService.ExtractAndRegister(text, turn);
                    if (tags.Count > 0) ResearchTagsExtracted?.Invoke(this,
                        new ResearchTagsExtractedEventArgs { TotalCount = _researchService.Entries.Count, NewTags = tags });
                }
                if (final) { _activeRecord = null; return $"最大ターン {config.MaxTurns} に到達。最終生成を記録しました。"; }
                string transfer = BuildTransferText(config, turn, phase, source.SiteName, target.SiteName,
                    left ? config.RightSystemPrompt : config.LeftSystemPrompt, text);
                if (config.RequireApproval)
                {
                    NotifyStatus($"ターン {turn}: 承認待ち...");
                    var approval = await _approvalQueue.EnqueueAsync(source.SiteName, target.SiteName, transfer, true, ct);
                    if (!approval.Approved) { record.Status = "承認却下・未送信"; _activeRecord = null; return "承認却下"; }
                    transfer = approval.Text;
                }
                bool sent = false;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (attempt > 0) await Task.Delay(2000, ct);
                    NotifyStatus($"ターン {turn}: {target.SiteName} へ送信中 ({attempt + 1}/3)...");
                    var next = await ConversationMonitor.PrepareAsync(target, tgt, ct);
                    var result = await _transferService.TransferAsync(src, tgt, srcUrl, tgtUrl,
                        submit: true, appendBridge: false, manualText: transfer);
                    if (!result.Success) { NotifyStatus(result.Message); continue; }
                    next.Accepted = true; pending[tgt] = next; inputs[tgt] = transfer;
                    record.Submitted = true; record.Status = "送信受理確認済み";
                    _activeRecord = null; sent = true; break;
                }
                if (!sent) throw new InvalidOperationException($"ターン {turn}: 入力・送信に3回失敗しました。");
                ct.ThrowIfCancellationRequested();
                NotifyStatus($"ターン {turn}: 送信受理確認済み。");
                // The actual destination speaks next; phase controls the role, not routing.
                left = !left; phase = AdvancePhaseIndex(config.TurnPolicy, phase); turn++;
                await Task.Delay(Math.Max(0, config.TurnIntervalMs), ct);
            }
        }

        private IAiSiteAdapter RequireAdapter(string url)
            => _adapterResolver.Resolve(url) ?? throw new InvalidOperationException("サイトのアダプタが見つかりません: " + url);
        private static async Task<GenerationRequest> SendCheckedAsync(IAiSiteAdapter adapter,
            WebView2 view, string text, CancellationToken ct)
        {
            var request = await ConversationMonitor.PrepareAsync(adapter, view, ct);
            if (!await view.Dispatcher.InvokeAsync(() => adapter.SetInputAsync(view, text)).Task.Unwrap())
                throw new InvalidOperationException(adapter.SiteName + "への入力照合に失敗しました。");
            ct.ThrowIfCancellationRequested();
            if (!await view.Dispatcher.InvokeAsync(() => adapter.SendAsync(view)).Task.Unwrap())
                throw new InvalidOperationException(adapter.SiteName + "への送信に失敗しました。");
            request.Accepted = true;
            ct.ThrowIfCancellationRequested();
            return request;
        }

        private async Task<string?> WaitForHumanPriorityAsync(AutoDebateConfig config,
            List<TransferRecord> records, int turn, CancellationToken ct)
        {
            var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _humanInputTcs = pending;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(config.HumanPriorityTimeoutMs);
            var token = timeout.Token;
            try
            {
                ct.ThrowIfCancellationRequested();
                HumanPriorityInputRequired?.Invoke(this, new HumanPriorityInputRequest {
                    Summary = _summaryService.BuildModeratorSummary(records), DisplayName = "HumanPriority",
                    TimeoutMs = config.HumanPriorityTimeoutMs,
                    OnInputReady = text => { if (!token.IsCancellationRequested) pending.TrySetResult(text); } });
                return await pending.Task.WaitAsync(token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
            finally { if (ReferenceEquals(_humanInputTcs, pending)) _humanInputTcs = null; }
        }

        private async Task RunThirdSeatTurnAsync(AutoDebateConfig config, List<TransferRecord> records, CancellationToken ct)
        {
            var third = config.ThirdSeat;
            string thirdText;
            if (third.Mode == ThirdSeatMode.Human)
            {
                var input = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                ThirdSeatInputRequired?.Invoke(this, new ThirdSeatInputRequest {
                    Summary = _summaryService.BuildModeratorSummary(records), Role = third.Role,
                    DisplayName = third.DisplayName,
                    OnInputReady = text => { if (!ct.IsCancellationRequested) input.TrySetResult(text ?? string.Empty); } });
                thirdText = await input.Task.WaitAsync(ct);
            }
            else if (third.Mode == ThirdSeatMode.AiSite && third.WebView != null)
            {
                var adapter = RequireAdapter(third.Url);
                string prompt = third.SystemPrompt + "\n\n" + _summaryService.BuildModeratorSummary(records);
                var request = await SendCheckedAsync(adapter, third.WebView, prompt, ct);
                using var monitor = new ConversationMonitor(adapter, third.WebView);
                thirdText = await monitor.WaitForCompletionAsync(string.Empty, config.GenerationTimeoutMs, ct, request);
            }
            else thirdText = third.StaticText;
            ct.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(thirdText))
                NotifyStatus($"第3席 [{third.DisplayName}]: {_summaryService.Summarize(thirdText)}");
        }

        private static string BuildTransferText(AutoDebateConfig config, int turn, int phase,
            string source, string target, string prompt, string text)
        {
            string header = string.IsNullOrWhiteSpace(prompt) ? string.Empty : prompt.Trim();
            if (header.Length > 0 && !header.StartsWith("【なりきり設定】", StringComparison.Ordinal))
                header = "【なりきり設定】\n人物像:\n" + header + "\n上記の人物像の視点で、議題から逸れずに応答してください。";
            if (header.Length > 0) header += "\n\n";
            string role = config.TurnPolicy switch {
                TurnPolicy.CritiqueThenRefine => new[] { " 提案", " 批判", " 改善" }[phase % 3],
                TurnPolicy.ResearchReviewLoop => new[] { " 仮説", " 証明案", " 反例", " 査読" }[phase % 4],
                TurnPolicy.HumanPriority => " AI", _ => string.Empty };
            string result = header + $"[Turn {turn} {source}→{target}{role}]\n" + text;
            if (config.AppendBridge) result += "\n\nこの意見についてどう考えますか？";
            if (config.MaxTurns > 0 && turn + 1 == config.MaxTurns)
                result += "\n\n【最終ターン指示】ここまでの議論全体を踏まえ、議題に対する最終成案を示してください。結論、採用案、理由、実行ステップまたは判断基準を整理してください。";
            return result;
        }
        private static int AdvancePhaseIndex(TurnPolicy policy, int phase) => policy switch {
            TurnPolicy.CritiqueThenRefine => (phase + 1) % 3,
            TurnPolicy.ResearchReviewLoop => (phase + 1) % 4, _ => phase };
        private static bool ShouldInsertThirdSeat(ThirdSeatConfig third, int turn)
            => third.Mode != ThirdSeatMode.Disabled && third.IntervalTurns > 0
                && turn > 1 && (turn - 1) % third.IntervalTurns == 0;
        private static string CleanGeneratedText(string text)
        {
            // Remove only a leading transport prefix, never quoted instructions inside an answer.
            return Regex.Replace(text, @"\A\s*\[Turn\s+\d+[^\]]*\]\s*\r?\n?", string.Empty).Trim();
        }
        private static string StripHtml(string text)
        {
            if (!Regex.IsMatch(text, @"</?(?:span|div|p|br|math|mrow|annotation)\b", RegexOptions.IgnoreCase)) return text.Trim();
            text = Regex.Replace(text, @"<br\s*/?>|</p\s*>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<[^>]+>", string.Empty);
            return System.Net.WebUtility.HtmlDecode(text).Trim();
        }
        private void NotifyStatus(string text) => StatusChanged?.Invoke(this, text);
    }
    public sealed class ThirdSeatInputRequest
    {
        public string Summary { get; init; } = string.Empty;
        public DebateRole Role { get; init; }
        public string DisplayName { get; init; } = string.Empty;
        public Action<string?>? OnInputReady { get; init; }
    }
    public sealed class HumanPriorityInputRequest
    {
        public string Summary { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public int TimeoutMs { get; init; }
        public Action<string?>? OnInputReady { get; init; }
    }
    public sealed class ResearchTagsExtractedEventArgs : EventArgs
    {
        public int TotalCount { get; init; }
        public List<ResearchTagEntry> NewTags { get; init; } = new();
    }
}
