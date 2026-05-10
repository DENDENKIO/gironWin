using Microsoft.Web.WebView2.Wpf;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace gironWin
{
    public sealed class ConversationMonitor : IDisposable
    {
        public event EventHandler<GenerationDoneEventArgs>? GenerationDone;

        private readonly IAiSiteAdapter _adapter;
        private readonly WebView2       _webView;
        private bool _disposed;

        // ── チューニング定数 ──
        private const int    PollIntervalMs          = 300;
        private const int    StableQuietMs           = 5000;  // ★ 3500→5000ms
        private const int    AfterStopBufferMs        = 1200;  // ★ 800→1200ms
        private const int    MinMeaningfulLen         = 30;
        private const int    MaxConsecutiveFail       = 5;

        /// <summary>
        /// テキストが減った後、静止タイマーをリセットするための追加待機(ms)。
        /// AIの折りたたみ→展開の完了を待つ。
        /// </summary>
        private const int    ShrinkRecoveryWaitMs     = 5000;  // ★ 新規: 縮小後5秒追加待機

        /// <summary>
        /// 完了判定前にIsGenerating=falseを二重確認する間隔(ms)と回数。
        /// </summary>
        private const int    DoubleCheckIntervalMs    = 5000;  // ★ 新規: 5秒後に再確認
        private const int    DoubleCheckCount         = 2;     // ★ 新規: 2回確認

        /// <summary>
        /// snapshotLen > 0 のとき、現テキストがスナップショットを超えた後でないと完了判定しない。
        /// </summary>
        private const double OverSnapshotRatio        = 1.05;

        /// <summary>スナップショット超え待機の最大時間(ms)。</summary>
        private const int    ExceedSnapshotTimeoutMs  = 30000;

        private const double ShrinkRatioGuard         = 0.15;
        private const double RecoveryRatioThreshold   = 0.55;

        public ConversationMonitor(IAiSiteAdapter adapter, WebView2 webView)
        {
            _adapter = adapter;
            _webView = webView;
        }

        public async Task<string> WaitForCompletionAsync(
            string snapshot,
            int timeoutMs,
            CancellationToken ct = default)
        {
            if (_webView?.CoreWebView2 == null)
            {
                AppLogger.Warn(LogCategory.Monitor,
                    $"[{_adapter.SiteName}] CoreWebView2 が null → 即時リターン");
                return string.Empty;
            }

            string normalizedSnapshot = (snapshot ?? string.Empty).Trim();
            int    snapshotLen        = normalizedSnapshot.Length;

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(timeoutMs);

            string   lastText         = normalizedSnapshot;
            int      lastLength       = lastText.Length;
            DateTime lastChangedAt    = DateTime.UtcNow;
            bool     seenNewText      = false;
            bool     wasGenerating    = false;
            bool     exceededSnapshot = snapshotLen == 0;
            int      failCount        = 0;
            int      pollCount        = 0;
            // ★ 追加: 縮小発生時刻（DateTime.MinValue = 縮小未発生）
            DateTime lastShrinkAt     = DateTime.MinValue;
            // ★ 追加: テキストが一度でも増加したか（折りたたみ前の最大長）
            int      peakLength       = snapshotLen;

            AppLogger.Debug(LogCategory.Monitor,
                $"[{_adapter.SiteName}] WaitForCompletion 開始 " +
                $"snapshotLen={snapshotLen} timeoutMs={timeoutMs}");

            try
            {
                while (!linkedCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(PollIntervalMs, linkedCts.Token);
                    pollCount++;

                    // ── テキスト取得 ──
                    string latestText = string.Empty;
                    try
                    {
                        latestText = await _webView.Dispatcher.InvokeAsync(
                            async () =>
                                (await _adapter.ExtractLatestAsync(_webView))?.Trim()
                                ?? string.Empty
                        ).Task.Unwrap();
                        failCount = 0;
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        AppLogger.Warn(LogCategory.Monitor,
                            $"[{_adapter.SiteName}] ExtractLatest 失敗 " +
                            $"#{failCount}/{MaxConsecutiveFail}: {ex.Message}");
                        if (failCount >= MaxConsecutiveFail)
                        {
                            AppLogger.Error(LogCategory.Monitor,
                                $"[{_adapter.SiteName}] ExtractLatest 連続失敗 → 中断");
                            break;
                        }
                        continue;
                    }

                    // ── 生成中フラグ取得 ──
                    bool isGenerating = false;
                    try
                    {
                        isGenerating = await _webView.Dispatcher.InvokeAsync(
                            async () => await _adapter.IsGeneratingAsync(_webView)
                        ).Task.Unwrap();
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Debug(LogCategory.Monitor,
                            $"[{_adapter.SiteName}] IsGenerating 取得失敗: {ex.Message}");
                    }

                    int latestLen = latestText.Length;

                    // ── スナップショット超え判定 ──
                    if (!exceededSnapshot && snapshotLen > 0)
                    {
                        if (latestLen > snapshotLen * OverSnapshotRatio)
                        {
                            exceededSnapshot = true;
                            AppLogger.Debug(LogCategory.Monitor,
                                $"[{_adapter.SiteName}] スナップショット超え確認 " +
                                $"{snapshotLen}→{latestLen}文字 " +
                                $"(ratio={(double)latestLen / snapshotLen:P0})");
                            lastChangedAt = DateTime.UtcNow;
                            lastText      = latestText;
                            lastLength    = latestLen;
                            peakLength    = latestLen;
                        }
                        else
                        {
                            double waitedMs = (DateTime.UtcNow - lastChangedAt).TotalMilliseconds;
                            if (waitedMs > ExceedSnapshotTimeoutMs)
                            {
                                AppLogger.Warn(LogCategory.Monitor,
                                    $"[{_adapter.SiteName}] スナップショット超え待機タイムアウト {waitedMs:F0}ms → 強制許可 " +
                                    $"snapshot={snapshotLen} current={latestLen}");
                                exceededSnapshot = true;
                                lastChangedAt = DateTime.UtcNow;
                                lastText      = latestText;
                                lastLength    = latestLen;
                                peakLength    = latestLen;
                            }
                            else
                            {
                                if (pollCount % 10 == 0)
                                {
                                    AppLogger.Debug(LogCategory.Monitor,
                                        $"[{_adapter.SiteName}] poll#{pollCount} " +
                                        $"スナップショット超え待機中 snapshot={snapshotLen} current={latestLen} " +
                                        $"isGenerating={isGenerating} waited={waitedMs:F0}ms/{ExceedSnapshotTimeoutMs}ms");
                                }
                                if (isGenerating) wasGenerating = true;
                                continue;
                            }
                        }
                    }

                    // ── 縮小ガード ──
                    bool isShrinking  = snapshotLen > 0
                        && latestLen > 0
                        && latestText != normalizedSnapshot
                        && (double)latestLen / snapshotLen < ShrinkRatioGuard;

                    bool isRecovering = !isShrinking
                        && snapshotLen > 0
                        && latestLen > 0
                        && !exceededSnapshot
                        && (double)latestLen / snapshotLen < RecoveryRatioThreshold;

                    // ★ 追加: テキストが前回より減った場合（折りたたみ検出）
                    bool isDecreasing = seenNewText && latestLen < lastLength;

                    bool hasNewText = !string.IsNullOrWhiteSpace(latestText)
                                      && latestText != normalizedSnapshot
                                      && latestLen >= MinMeaningfulLen
                                      && !isShrinking
                                      && !isRecovering;

                    if (pollCount % 10 == 0)
                    {
                        AppLogger.Debug(LogCategory.Monitor,
                            $"[{_adapter.SiteName}] poll#{pollCount} " +
                            $"isGenerating={isGenerating} hasNewText={hasNewText} " +
                            $"textLen={latestLen} peak={peakLength} " +
                            $"shrinking={isShrinking} recovering={isRecovering} decreasing={isDecreasing} " +
                            $"exceededSnap={exceededSnapshot} " +
                            $"quietMs={(DateTime.UtcNow - lastChangedAt).TotalMilliseconds:F0}");
                    }

                    if (!hasNewText)
                    {
                        if (isGenerating) wasGenerating = true;

                        if (isShrinking || isRecovering)
                        {
                            string label = isShrinking ? "縮小中スキップ" : "回復中スキップ";
                            AppLogger.Debug(LogCategory.Monitor,
                                $"[{_adapter.SiteName}] {label} " +
                                $"{snapshotLen}→{latestLen}文字 " +
                                $"(ratio={(double)latestLen / snapshotLen:P0})");
                            // ★ 縮小/回復中はタイマーリセットして待機継続
                            lastChangedAt = DateTime.UtcNow;
                            lastShrinkAt  = DateTime.UtcNow;
                            lastText      = latestText;
                            lastLength    = latestLen;
                        }
                        continue;
                    }

                    seenNewText = true;
                    if (isGenerating) wasGenerating = true;
                    if (latestLen > peakLength) peakLength = latestLen;

                    // ── テキスト変化検出 ──
                    bool changed = latestLen != lastLength || latestText != lastText;

                    if (isDecreasing)
                    {
                        // ★ テキストが減った（折りたたみ）→ タイマーリセット＋追加待機フラグ
                        AppLogger.Debug(LogCategory.Monitor,
                            $"[{_adapter.SiteName}] テキスト減少（折りたたみ？） " +
                            $"{lastLength}→{latestLen}文字 → タイマーリセット");
                        lastText      = latestText;
                        lastLength    = latestLen;
                        lastChangedAt = DateTime.UtcNow;
                        lastShrinkAt  = DateTime.UtcNow;
                        continue;
                    }

                    if (changed)
                    {
                        AppLogger.Debug(LogCategory.Monitor,
                            $"[{_adapter.SiteName}] テキスト変化 {lastLength}→{latestLen}文字");
                        lastText      = latestText;
                        lastLength    = latestLen;
                        lastChangedAt = DateTime.UtcNow;
                        continue;
                    }

                    double quietMs = (DateTime.UtcNow - lastChangedAt).TotalMilliseconds;

                    // ★ 縮小直後のクールダウン: 縮小から ShrinkRecoveryWaitMs 経過していない場合は完了判定しない
                    bool inShrinkCooldown = lastShrinkAt != DateTime.MinValue
                        && (DateTime.UtcNow - lastShrinkAt).TotalMilliseconds < ShrinkRecoveryWaitMs;

                    if (inShrinkCooldown)
                    {
                        double shrinkElapsed = (DateTime.UtcNow - lastShrinkAt).TotalMilliseconds;
                        if (pollCount % 10 == 0)
                        {
                            AppLogger.Debug(LogCategory.Monitor,
                                $"[{_adapter.SiteName}] 縮小後クールダウン中 " +
                                $"{shrinkElapsed:F0}ms/{ShrinkRecoveryWaitMs}ms (isGenerating={isGenerating})");
                        }
                        continue;
                    }

                    // ── 完了判定1: IsGenerating が true→false ──
                    if (wasGenerating && !isGenerating && seenNewText)
                    {
                        // ★ 二重確認: 5秒後にもう一度チェック
                        bool confirmedDone = await DoubleCheckCompletionAsync(
                            linkedCts.Token, DoubleCheckCount, DoubleCheckIntervalMs);

                        if (!confirmedDone)
                        {
                            AppLogger.Debug(LogCategory.Monitor,
                                $"[{_adapter.SiteName}] 完了二重確認で再生成検出 → 待機継続");
                            wasGenerating = true;
                            lastChangedAt = DateTime.UtcNow;
                            continue;
                        }

                        AppLogger.Info(LogCategory.Monitor,
                            $"[{_adapter.SiteName}] 完了判定: IsGenerating=false 二重確認OK (poll#{pollCount})");
                        await Task.Delay(AfterStopBufferMs, linkedCts.Token);

                        string finalText = latestText;
                        try
                        {
                            finalText = await _webView.Dispatcher.InvokeAsync(
                                async () =>
                                    (await _adapter.ExtractLatestAsync(_webView))?.Trim()
                                    ?? string.Empty
                            ).Task.Unwrap();
                        }
                        catch { finalText = latestText; }

                        if (string.IsNullOrWhiteSpace(finalText)) finalText = latestText;
                        AppLogger.Debug(LogCategory.Monitor,
                            $"[{_adapter.SiteName}] 最終テキスト取得 len={finalText.Length}");
                        return Complete(finalText, normalizedSnapshot);
                    }

                    // ── 完了判定2: StableQuietMs 間変化なし ──
                    if (seenNewText && quietMs >= StableQuietMs)
                    {
                        // ★ 二重確認: さらに5秒後にテキスト変化がないか確認
                        bool confirmedStable = await DoubleCheckStabilityAsync(
                            latestText, linkedCts.Token, DoubleCheckIntervalMs);

                        if (!confirmedStable)
                        {
                            AppLogger.Debug(LogCategory.Monitor,
                                $"[{_adapter.SiteName}] StableQuiet 二重確認で変化検出 → 待機継続");
                            lastChangedAt = DateTime.UtcNow;
                            continue;
                        }

                        AppLogger.Info(LogCategory.Monitor,
                            $"[{_adapter.SiteName}] 完了判定: " +
                            $"StableQuiet {quietMs:F0}ms >= {StableQuietMs}ms 二重確認OK (poll#{pollCount})");
                        return Complete(latestText, normalizedSnapshot);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                AppLogger.Warn(LogCategory.Monitor,
                    $"[{_adapter.SiteName}] WaitForCompletion タイムアウト/キャンセル (poll#{pollCount})");
            }

            // ── タイムアウト fallback ──
            AppLogger.Warn(LogCategory.Monitor,
                $"[{_adapter.SiteName}] タイムアウト fallback 開始");
            string fallback = string.Empty;
            try
            {
                fallback = await _webView.Dispatcher.InvokeAsync(
                    async () =>
                        (await _adapter.ExtractLatestAsync(_webView))?.Trim()
                        ?? string.Empty
                ).Task.Unwrap();
            }
            catch (Exception ex)
            {
                AppLogger.Error(LogCategory.Monitor,
                    $"[{_adapter.SiteName}] fallback ExtractLatest 失敗", ex);
            }

            if (!string.IsNullOrWhiteSpace(fallback) && fallback != normalizedSnapshot)
            {
                AppLogger.Info(LogCategory.Monitor,
                    $"[{_adapter.SiteName}] fallback テキスト採用 len={fallback.Length}");
                return Complete(fallback, normalizedSnapshot);
            }

            AppLogger.Warn(LogCategory.Monitor,
                $"[{_adapter.SiteName}] fallback も空 → string.Empty を返す");
            return string.Empty;
        }

        // ★ 新規: IsGenerating=false を DoubleCheckCount 回連続確認
        private async Task<bool> DoubleCheckCompletionAsync(
            CancellationToken ct, int checkCount, int intervalMs)
        {
            for (int i = 0; i < checkCount; i++)
            {
                try { await Task.Delay(intervalMs, ct); }
                catch (OperationCanceledException) { return true; }

                bool isGen = false;
                try
                {
                    isGen = await _webView.Dispatcher.InvokeAsync(
                        async () => await _adapter.IsGeneratingAsync(_webView)
                    ).Task.Unwrap();
                }
                catch { }

                // テキストも再取得して変化確認
                string cur = string.Empty;
                try
                {
                    cur = await _webView.Dispatcher.InvokeAsync(
                        async () =>
                            (await _adapter.ExtractLatestAsync(_webView))?.Trim()
                            ?? string.Empty
                    ).Task.Unwrap();
                }
                catch { }

                AppLogger.Debug(LogCategory.Monitor,
                    $"[{_adapter.SiteName}] DoubleCheckCompletion #{i + 1}/{checkCount} " +
                    $"isGenerating={isGen} textLen={cur.Length}");

                if (isGen) return false; // まだ生成中 → 完了でない
            }
            return true;
        }

        // ★ 新規: テキストが安定しているか DoubleCheckIntervalMs 後に再確認
        private async Task<bool> DoubleCheckStabilityAsync(
            string baseText, CancellationToken ct, int waitMs)
        {
            try { await Task.Delay(waitMs, ct); }
            catch (OperationCanceledException) { return true; }

            string cur = string.Empty;
            try
            {
                cur = await _webView.Dispatcher.InvokeAsync(
                    async () =>
                        (await _adapter.ExtractLatestAsync(_webView))?.Trim()
                        ?? string.Empty
                ).Task.Unwrap();
            }
            catch { }

            bool isGen = false;
            try
            {
                isGen = await _webView.Dispatcher.InvokeAsync(
                    async () => await _adapter.IsGeneratingAsync(_webView)
                ).Task.Unwrap();
            }
            catch { }

            AppLogger.Debug(LogCategory.Monitor,
                $"[{_adapter.SiteName}] DoubleCheckStability " +
                $"isGenerating={isGen} baseLen={baseText.Length} curLen={cur.Length} " +
                $"textChanged={cur != baseText}");

            // テキスト変化 or まだ生成中 → 安定していない
            if (isGen || cur != baseText) return false;

            return true;
        }

        private string Complete(string text, string snapshot)
        {
            if (text == snapshot) return string.Empty;
            AppLogger.Debug(LogCategory.Monitor,
                $"[{_adapter.SiteName}] Complete len={text.Length}");
            GenerationDone?.Invoke(this, new GenerationDoneEventArgs(_adapter.SiteName, text));
            return text;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }

    public sealed class GenerationDoneEventArgs : EventArgs
    {
        public string SiteName { get; }
        public string Text     { get; }
        public GenerationDoneEventArgs(string siteName, string text)
        {
            SiteName = siteName;
            Text     = text;
        }
    }
}
