using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows; // Dispatcher用

namespace gironWin
{
    /// <summary>
    /// AI 応答の生成完了を監視する。
    /// 刷新: MutationObserver に依存せず、UIスレッド安全な単一ループで監視を行う。
    /// </summary>
    public sealed class ConversationMonitor : IDisposable
    {
        public event EventHandler<GenerationDoneEventArgs>? GenerationDone;

        private readonly IAiSiteAdapter _adapter;
        private readonly WebView2      _webView;
        private bool _disposed;

        // ── チューニング定数 ──────────────────────────────
        private const int PollIntervalMs     = 300;   // ポーリング間隔
        private const int StableQuietMs      = 1500;  // テキスト変化なし→完了とみなす静止時間
        private const int AfterStopBufferMs  = 600;   // IsGenerating=false 後の追加待機
        private const int MinMeaningfulLen   = 20;    // 有意テキストの最小長
        private const int MaxConsecutiveFail = 5;     // ExtractLatest が連続失敗したら諦める回数
        // ─────────────────────────────────────────────────

        public ConversationMonitor(IAiSiteAdapter adapter, WebView2 webView)
        {
            _adapter = adapter;
            _webView = webView;
        }

        /// <summary>
        /// 生成完了を待ち、完成テキストを返す。
        /// すべての処理を単一の async ループで完結させる（Task.Run不使用）。
        /// </summary>
        public async Task<string> WaitForCompletionAsync(
            string snapshot,
            int timeoutMs,
            CancellationToken ct = default)
        {
            if (_webView?.CoreWebView2 == null)
                return string.Empty;

            string normalizedSnapshot = (snapshot ?? string.Empty).Trim();

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(timeoutMs);

            string lastText       = normalizedSnapshot;
            int    lastLength     = lastText.Length;
            DateTime lastChangedAt = DateTime.UtcNow;
            bool   seenNewText    = false;
            bool   wasGenerating  = false;
            int    failCount      = 0;

            try
            {
                while (!linkedCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(PollIntervalMs, linkedCts.Token);

                    // ─── テキスト取得（UIスレッドで実行） ───
                    string latestText = string.Empty;
                    try
                    {
                        latestText = await _webView.Dispatcher.InvokeAsync(
                            async () => (await _adapter.ExtractLatestAsync(_webView))?.Trim() ?? string.Empty
                        ).Task.Unwrap();
                        failCount = 0;
                    }
                    catch
                    {
                        failCount++;
                        if (failCount >= MaxConsecutiveFail)
                        {
                            // WebView が応答しない → fallback なしで空を返す
                            break;
                        }
                        continue;
                    }

                    // ─── 生成中フラグ取得 ───
                    bool isGenerating = false;
                    try
                    {
                        isGenerating = await _webView.Dispatcher.InvokeAsync(
                            async () => await _adapter.IsGeneratingAsync(_webView)
                        ).Task.Unwrap();
                    }
                    catch { }

                    bool hasNewText = !string.IsNullOrWhiteSpace(latestText)
                                      && latestText != normalizedSnapshot
                                      && latestText.Length >= MinMeaningfulLen;

                    if (!hasNewText)
                    {
                        // まだ新テキストが出ていない → タイマーリセット
                        lastChangedAt = DateTime.UtcNow;
                        if (isGenerating) wasGenerating = true;
                        continue;
                    }

                    seenNewText = true;
                    if (isGenerating) wasGenerating = true;

                    // ─── テキスト変化チェック ───
                    bool changed = (latestText.Length != lastLength) || (latestText != lastText);
                    if (changed)
                    {
                        lastText      = latestText;
                        lastLength    = latestText.Length;
                        lastChangedAt = DateTime.UtcNow;
                        continue;
                    }

                    double quietMs = (DateTime.UtcNow - lastChangedAt).TotalMilliseconds;

                    // ─── 完了判定パターン1: IsGenerating が true→false ───
                    if (wasGenerating && !isGenerating && seenNewText)
                    {
                        await Task.Delay(AfterStopBufferMs, linkedCts.Token);
                        // 最終テキストを再取得
                        string finalText = string.Empty;
                        try
                        {
                            finalText = await _webView.Dispatcher.InvokeAsync(
                                async () => (await _adapter.ExtractLatestAsync(_webView))?.Trim() ?? string.Empty
                            ).Task.Unwrap();
                        }
                        catch { finalText = latestText; }

                        if (string.IsNullOrWhiteSpace(finalText)) finalText = latestText;
                        return Complete(finalText, normalizedSnapshot);
                    }

                    // ─── 完了判定パターン2: StableQuietMs 間テキスト変化なし ───
                    if (seenNewText && quietMs >= StableQuietMs)
                    {
                        return Complete(latestText, normalizedSnapshot);
                    }
                }
            }
            catch (OperationCanceledException) { }

            // ─── タイムアウト fallback ───
            string fallback = string.Empty;
            try
            {
                fallback = await _webView.Dispatcher.InvokeAsync(
                    async () => (await _adapter.ExtractLatestAsync(_webView))?.Trim() ?? string.Empty
                ).Task.Unwrap();
            }
            catch { }

            if (!string.IsNullOrWhiteSpace(fallback) && fallback != normalizedSnapshot)
                return Complete(fallback, normalizedSnapshot);

            return string.Empty;
        }

        private string Complete(string text, string snapshot)
        {
            if (string.IsNullOrWhiteSpace(text) || text == snapshot)
                return string.Empty;
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
            Text = text;
        }
    }
}
