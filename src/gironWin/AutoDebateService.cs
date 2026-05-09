using Microsoft.Web.WebView2.Wpf;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace gironWin
{
    public enum DebateDirection
    {
        LeftToRight,
        RightToLeft
    }

    public sealed class AutoDebateService
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

        public void Start(AutoDebateConfig config)
        {
            if (IsRunning) return;

            _cts = new CancellationTokenSource();
            _isPaused = false;
            _pauseTcs = null;

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

        private async Task RunLoopAsync(AutoDebateConfig config, CancellationToken ct)
        {
            NotifyStatus("自動討論を開始します。");
            int turn = 0;
            DebateDirection direction = DebateDirection.LeftToRight;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (_isPaused)
                    {
                        NotifyStatus("一時停止中... 再開ボタンを押してください。");
                        await (_pauseTcs?.Task ?? Task.CompletedTask);
                        if (ct.IsCancellationRequested) break;
                    }

                    turn++;
                    TurnAdvanced?.Invoke(this, turn);

                    bool isLeftTurn = direction == DebateDirection.LeftToRight;

                    var srcWebView = isLeftTurn ? config.LeftWebView : config.RightWebView;
                    var tgtWebView = isLeftTurn ? config.RightWebView : config.LeftWebView;

                    string srcUrl = isLeftTurn ? config.LeftUrl : config.RightUrl;
                    string tgtUrl = isLeftTurn ? config.RightUrl : config.LeftUrl;

                    var srcAdapter = _adapterResolver.Resolve(srcUrl);
                    var tgtAdapter = _adapterResolver.Resolve(tgtUrl);

                    if (srcAdapter == null || tgtAdapter == null)
                    {
                        NotifyStatus("アダプタが見つかりません。停止します。");
                        break;
                    }

                    NotifyStatus($"ターン {turn} [{srcAdapter.SiteName} → {tgtAdapter.SiteName}]: 生成完了を待機中...");

                    string snapshot = (await srcAdapter.ExtractLatestAsync(srcWebView))?.Trim() ?? string.Empty;
                    NotifyStatus($"ターン {turn}: 監視開始時文字数 {snapshot.Length}");

                    string generatedText;
                    try
                    {
                        using var monitor = new ConversationMonitor(srcAdapter, srcWebView);
                        generatedText = await monitor.WaitForCompletionAsync(
                            snapshot,
                            config.GenerationTimeoutMs,
                            ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    generatedText = generatedText?.Trim() ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(generatedText) || generatedText == snapshot)
                    {
                        NotifyStatus($"ターン {turn}: 新規テキストを検出できませんでした。再試行します。");
                        await Task.Delay(1500, ct);
                        turn--;
                        continue;
                    }

                    NotifyStatus($"ターン {turn}: 生成完了（{generatedText.Length}文字）");

                    string transferText = config.AppendBridge
                        ? $"{generatedText}\n\nこの意見についてどう考えますか？"
                        : generatedText;

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
                                NotifyStatus($"ターン {turn}: 却下されました。停止します。");
                                break;
                            }

                            transferText = result.Text;
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }

                    NotifyStatus($"ターン {turn}: {tgtAdapter.SiteName} へ送信中...");

                    var transferResult = await _transferService.TransferAsync(
                        srcWebView,
                        tgtWebView,
                        srcUrl,
                        tgtUrl,
                        submit: true,
                        appendBridge: false,
                        manualText: transferText);

                    if (!transferResult.Success)
                    {
                        NotifyStatus($"ターン {turn}: 送信失敗 → {transferResult.Message}");
                        break;
                    }

                    NotifyStatus($"ターン {turn}: 送信完了。");

                    if (config.MaxTurns > 0 && turn >= config.MaxTurns)
                    {
                        NotifyStatus($"最大ターン数 {config.MaxTurns} に到達。自動討論終了。");
                        break;
                    }

                    direction = direction == DebateDirection.LeftToRight
                        ? DebateDirection.RightToLeft
                        : DebateDirection.LeftToRight;

                    await Task.Delay(config.TurnIntervalMs, ct);
                }
            }
            finally
            {
                _cts = null;
                _isPaused = false;
                _pauseTcs = null;
                DebateStopped?.Invoke(this, EventArgs.Empty);
                NotifyStatus("自動討論終了。");
            }
        }

        private void NotifyStatus(string message)
        {
            StatusChanged?.Invoke(this, message);
        }
    }

    public sealed class AutoDebateConfig
    {
        public WebView2 LeftWebView { get; set; } = null!;
        public WebView2 RightWebView { get; set; } = null!;
        public string LeftUrl { get; set; } = string.Empty;
        public string RightUrl { get; set; } = string.Empty;
        public bool AppendBridge { get; set; }
        public bool RequireApproval { get; set; } = true;
        public int MaxTurns { get; set; }
        public int TurnIntervalMs { get; set; } = 500;
        public int GenerationTimeoutMs { get; set; } = 45000;
    }
}
