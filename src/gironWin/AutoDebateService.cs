using Microsoft.Web.WebView2.Wpf;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace gironWin
{
    public enum DebateDirection { LeftToRight, RightToLeft }

    public class AutoDebateService
    {
        private readonly TransferService _transferService;
        private readonly ApprovalQueue _approvalQueue;
        private readonly AiSiteAdapterResolver _adapterResolver;

        private CancellationTokenSource? _cts;
        private bool _isPaused;
        private TaskCompletionSource<bool>? _pauseTcs;

        public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;
        public bool IsPaused => _isPaused;

        public event EventHandler<string>? StatusChanged;
        public event EventHandler<int>? TurnAdvanced;
        public event EventHandler? DebateStopped;

        public AutoDebateService(
            TransferService transferService,
            ApprovalQueue approvalQueue,
            AiSiteAdapterResolver adapterResolver)
        {
            _transferService = transferService;
            _approvalQueue = approvalQueue;
            _adapterResolver = adapterResolver;
        }

        // ---------------------------------------------------------------
        // 開始 / 停止 / 一時停止
        // ---------------------------------------------------------------

        public void Start(AutoDebateConfig config)
        {
            if (IsRunning) return;
            _cts = new CancellationTokenSource();
            _isPaused = false;
            _ = RunLoopAsync(config, _cts.Token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;
            _isPaused = false;
            _pauseTcs?.TrySetResult(true);
            NotifyStatus("討論を停止しました。");
            DebateStopped?.Invoke(this, EventArgs.Empty);
        }

        public void Pause()
        {
            if (!IsRunning || _isPaused) return;
            _isPaused = true;
            _pauseTcs = new TaskCompletionSource<bool>();
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

            // 最初は左→右
            DebateDirection direction = DebateDirection.LeftToRight;

            while (!ct.IsCancellationRequested)
            {
                // 一時停止
                if (_isPaused)
                {
                    NotifyStatus("一時停止中... 再開ボタンを押してください。");
                    await (_pauseTcs?.Task ?? Task.CompletedTask);
                    if (ct.IsCancellationRequested) break;
                }

                turn++;
                TurnAdvanced?.Invoke(this, turn);

                // 今ターンの送信元・送信先を決定
                bool isLeftTurn = direction == DebateDirection.LeftToRight;

                var srcWebView = isLeftTurn ? config.LeftWebView : config.RightWebView;
                var tgtWebView = isLeftTurn ? config.RightWebView : config.LeftWebView;
                string srcUrl   = isLeftTurn ? config.LeftUrl    : config.RightUrl;
                string tgtUrl   = isLeftTurn ? config.RightUrl   : config.LeftUrl;

                var srcAdapter = _adapterResolver.Resolve(srcUrl);
                var tgtAdapter = _adapterResolver.Resolve(tgtUrl);

                if (srcAdapter == null || tgtAdapter == null)
                {
                    NotifyStatus("アダプタが見つかりません。停止します。");
                    break;
                }

                NotifyStatus($"ターン {turn} [{srcAdapter.SiteName} → {tgtAdapter.SiteName}]: 生成完了を待機中...");

                // ① 送信元の現在テキストをスナップショット（監視開始基準点）
                string snapshot = await srcAdapter.ExtractLatestAsync(srcWebView);

                // ② 生成完了を待つ
                string generatedText;
                try
                {
                    generatedText = await WaitForNewGenerationAsync(
                        srcAdapter, srcWebView, snapshot, config.GenerationTimeoutMs, ct);
                }
                catch (OperationCanceledException) { break; }

                if (string.IsNullOrWhiteSpace(generatedText))
                {
                    NotifyStatus($"ターン {turn}: テキスト取得失敗。再試行します。");
                    // ループを折り返さずリトライ（同方向のまま）
                    await Task.Delay(1500, ct);
                    continue;
                }

                NotifyStatus($"ターン {turn}: テキスト取得完了（{generatedText.Length}文字）");

                // ③ 橋渡し文付加
                string transferText = config.AppendBridge
                    ? $"{generatedText}\n\nこの意見についてどう考えますか？"
                    : generatedText;

                // ④ 承認確認
                if (config.RequireApproval)
                {
                    NotifyStatus($"ターン {turn}: 承認待ち...");
                    try
                    {
                        var result = await _approvalQueue.EnqueueAsync(
                            srcAdapter.SiteName,
                            tgtAdapter.SiteName,
                            transferText,
                            true,
                            ct);

                        if (!result.Approved)
                        {
                            NotifyStatus($"ターン {turn}: 却下。方向を反転せずスキップ。");
                            // 却下時は同じ方向をリトライするか停止するか選択
                            // ここでは停止
                            Stop();
                            return;
                        }

                        transferText = result.Text;
                    }
                    catch (OperationCanceledException) { break; }
                }

                // ⑤ 送信先へ転送（submit = true で自動送信）
                NotifyStatus($"ターン {turn}: {tgtAdapter.SiteName} へ送信中...");

                var transferResult = await _transferService.TransferAsync(
                    srcWebView, tgtWebView, srcUrl, tgtUrl,
                    submit: true,
                    appendBridge: false,          // 橋渡し文は③で付加済み
                    manualText: transferText);

                if (!transferResult.Success)
                {
                    NotifyStatus($"ターン {turn}: 送信失敗 → {transferResult.Message}。停止します。");
                    break;
                }

                NotifyStatus($"ターン {turn}: 送信完了。");

                // ⑥ 最大ターン数チェック
                if (config.MaxTurns > 0 && turn >= config.MaxTurns)
                {
                    NotifyStatus($"最大ターン数 {config.MaxTurns} に到達。自動討論終了。");
                    break;
                }

                // ⑦ 方向反転（左→右 の次は 右→左）
                direction = direction == DebateDirection.LeftToRight
                    ? DebateDirection.RightToLeft
                    : DebateDirection.LeftToRight;

                // ターン間インターバル（送信先が生成を開始するまでの余裕）
                await Task.Delay(config.TurnIntervalMs, ct);
            }

            _cts = null;
            DebateStopped?.Invoke(this, EventArgs.Empty);
            NotifyStatus("自動討論終了。");
        }

        // ---------------------------------------------------------------
        // 新しい生成完了を待機（スナップショット方式）
        // ---------------------------------------------------------------

        private async Task<string> WaitForNewGenerationAsync(
            IAiSiteAdapter adapter,
            WebView2 webView,
            string snapshot,
            int timeoutMs,
            CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            using var monitor = new ConversationMonitor(adapter, webView);
            monitor.GenerationDone += (_, e) => tcs.TrySetResult(e.Text);

            // スナップショットを渡して監視開始
            await monitor.StartWatchingAsync(snapshot, ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeoutMs);
            timeoutCts.Token.Register(() => tcs.TrySetCanceled());

            try
            {
                return await tcs.Task;
            }
            catch (OperationCanceledException)
            {
                // タイムアウト or キャンセル
                if (ct.IsCancellationRequested) throw;

                // タイムアウトの場合は直接取得を試みる
                NotifyStatus("タイムアウト。直接テキスト取得を試みます。");
                string direct = await adapter.ExtractLatestAsync(webView);
                return direct != snapshot ? direct : string.Empty;
            }
        }

        private void NotifyStatus(string msg) =>
            StatusChanged?.Invoke(this, msg);
    }

    public sealed class AutoDebateConfig
    {
        public WebView2 LeftWebView  { get; set; } = null!;
        public WebView2 RightWebView { get; set; } = null!;
        public string LeftUrl        { get; set; } = string.Empty;
        public string RightUrl       { get; set; } = string.Empty;
        public bool AppendBridge     { get; set; } = false;
        public bool RequireApproval  { get; set; } = true;
        public int MaxTurns          { get; set; } = 0;
        public int TurnIntervalMs    { get; set; } = 2000;   // 送信後の待機
        public int GenerationTimeoutMs { get; set; } = 90000; // 90秒
    }
}
