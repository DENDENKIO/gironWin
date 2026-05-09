using Microsoft.Web.WebView2.Wpf;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace gironWin
{
    public enum DebateDirection { LeftToRight, RightToLeft }

    /// <summary>
    /// 自動往復討論ループを管理する。
    /// 生成完了 → 取得 → 承認判定 → 送信 → 次の監視 のサイクルを回す。
    /// </summary>
    public class AutoDebateService
    {
        private readonly TransferService _transferService;
        private readonly ApprovalQueue _approvalQueue;
        private readonly AiSiteAdapterResolver _adapterResolver;

        private CancellationTokenSource? _cts;
        private bool _isPaused;

        public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;
        public bool IsPaused => _isPaused;

        // UI への通知
        public event EventHandler<string>? StatusChanged;
        public event EventHandler<int>? TurnAdvanced;

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
            NotifyStatus("討論を停止しました。");
        }

        public void Pause()
        {
            _isPaused = true;
            NotifyStatus("討論を一時停止しました。");
        }

        public void Resume()
        {
            _isPaused = false;
            NotifyStatus("討論を再開しました。");
        }

        // ---------------------------------------------------------------
        // メインループ
        // ---------------------------------------------------------------

        private async Task RunLoopAsync(AutoDebateConfig config, CancellationToken ct)
        {
            NotifyStatus("自動討論を開始しました。");
            int turn = 0;
            DebateDirection direction = DebateDirection.LeftToRight;

            while (!ct.IsCancellationRequested)
            {
                // 一時停止待ち
                while (_isPaused && !ct.IsCancellationRequested)
                    await Task.Delay(300, ct);

                if (ct.IsCancellationRequested) break;

                turn++;
                TurnAdvanced?.Invoke(this, turn);
                NotifyStatus($"ターン {turn}: 生成完了を待機中...");

                // 現在のターンに応じて送信元・送信先を決定
                (var srcWebView, var tgtWebView, string srcUrl, string tgtUrl) =
                    direction == DebateDirection.LeftToRight
                        ? (config.LeftWebView, config.RightWebView, config.LeftUrl, config.RightUrl)
                        : (config.RightWebView, config.LeftWebView, config.RightUrl, config.LeftUrl);

                // 送信元の生成完了を待つ
                var srcAdapter = _adapterResolver.Resolve(srcUrl);
                if (srcAdapter == null)
                {
                    NotifyStatus("送信元アダプタが見つかりません。停止します。");
                    break;
                }

                string latestText;
                try
                {
                    latestText = await WaitForGenerationAsync(srcAdapter, srcWebView, config, ct);
                }
                catch (OperationCanceledException) { break; }

                if (string.IsNullOrWhiteSpace(latestText))
                {
                    NotifyStatus($"ターン {turn}: テキスト取得失敗。スキップします。");
                    direction = Flip(direction);
                    continue;
                }

                NotifyStatus($"ターン {turn}: テキスト取得完了。");

                // 橋渡し文付加
                string transferText = config.AppendBridge
                    ? $"{latestText}\n\nこのように考えていますがどうですか？"
                    : latestText;

                // 承認判定
                if (config.RequireApproval)
                {
                    NotifyStatus($"ターン {turn}: 承認待ち...");
                    try
                    {
                        var result = await _approvalQueue.EnqueueAsync(
                            srcAdapter.SiteName,
                            _adapterResolver.Resolve(tgtUrl)?.SiteName ?? tgtUrl,
                            transferText,
                            true,
                            ct);

                        if (!result.Approved)
                        {
                            NotifyStatus($"ターン {turn}: 却下されました。");
                            direction = Flip(direction);
                            continue;
                        }

                        transferText = result.Text;
                    }
                    catch (OperationCanceledException) { break; }
                }

                // 送信
                NotifyStatus($"ターン {turn}: 送信中...");
                var transferResult = await _transferService.TransferAsync(
                    srcWebView, tgtWebView, srcUrl, tgtUrl,
                    submit: true,
                    appendBridge: false,     // 橋渡し文はすでに付加済み
                    manualText: transferText);

                NotifyStatus($"ターン {turn}: {transferResult.Message}");

                if (!transferResult.Success)
                {
                    NotifyStatus($"ターン {turn}: 送信失敗。停止します。");
                    break;
                }

                // 停止条件チェック
                if (config.MaxTurns > 0 && turn >= config.MaxTurns)
                {
                    NotifyStatus($"最大ターン数 {config.MaxTurns} に到達しました。自動討論を終了します。");
                    break;
                }

                direction = Flip(direction);

                // ターン間インターバル
                await Task.Delay(config.TurnIntervalMs, ct);
            }

            _cts = null;
            NotifyStatus("自動討論ループを終了しました。");
        }

        // ---------------------------------------------------------------
        // 生成完了待機（Monitor + ポーリング併用）
        // ---------------------------------------------------------------

        private async Task<string> WaitForGenerationAsync(
            IAiSiteAdapter adapter,
            WebView2 webView,
            AutoDebateConfig config,
            CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<string>();

            var monitor = new ConversationMonitor(adapter, webView);
            monitor.GenerationDone += (_, e) => tcs.TrySetResult(e.Text);

            await monitor.StartWatchingAsync();

            // タイムアウト付きで待機
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(config.GenerationTimeoutMs);

            try
            {
                timeoutCts.Token.Register(() => tcs.TrySetCanceled());
                return await tcs.Task;
            }
            catch (OperationCanceledException)
            {
                monitor.StopWatching();
                // タイムアウト時はポーリングで直接取得を試みる
                if (!ct.IsCancellationRequested)
                {
                    NotifyStatus("Monitor タイムアウト。直接取得を試みます。");
                    return await adapter.ExtractLatestAsync(webView);
                }
                throw;
            }
        }

        private static DebateDirection Flip(DebateDirection d) =>
            d == DebateDirection.LeftToRight
                ? DebateDirection.RightToLeft
                : DebateDirection.LeftToRight;

        private void NotifyStatus(string msg) =>
            StatusChanged?.Invoke(this, msg);
    }

    // ---------------------------------------------------------------
    // 設定
    // ---------------------------------------------------------------

    public sealed class AutoDebateConfig
    {
        public WebView2 LeftWebView { get; set; } = null!;
        public WebView2 RightWebView { get; set; } = null!;
        public string LeftUrl { get; set; } = string.Empty;
        public string RightUrl { get; set; } = string.Empty;
        public bool AppendBridge { get; set; } = true;
        public bool RequireApproval { get; set; } = true;
        public int MaxTurns { get; set; } = 0;          // 0 = 無制限
        public int TurnIntervalMs { get; set; } = 1000;
        public int GenerationTimeoutMs { get; set; } = 60000; // 60秒
    }
}
