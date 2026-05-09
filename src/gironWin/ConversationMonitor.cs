using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace gironWin
{
    /// <summary>
    /// AI 生成完了を検知する。
    /// 方式①: MutationObserver（JS側からpostMessage）
    /// 方式②: C#ポーリング（500ms間隔で直接チェック） ← フォールバック
    /// 両方併用し、どちらか先に検知した方が勝つ。
    /// </summary>
    public class ConversationMonitor : IDisposable
    {
        public event EventHandler<GenerationDoneEventArgs>? GenerationDone;

        private readonly IAiSiteAdapter _adapter;
        private readonly WebView2 _webView;
        private bool _disposed;
        private bool _notified;

        // ポーリング設定
        private const int PollIntervalMs   = 500;   // チェック間隔
        private const int QuietStableCount = 4;     // 同じテキストが4回連続＝完了（2秒）
        private const int WaitForStartMs   = 3000;  // 生成開始を最大3秒待つ

        public ConversationMonitor(IAiSiteAdapter adapter, WebView2 webView)
        {
            _adapter = adapter;
            _webView = webView;
        }

        /// <summary>
        /// 監視を開始する。Observer + ポーリングの二重監視。
        /// snapshot: 監視開始直前の既存テキスト。
        /// </summary>
        public async Task StartWatchingAsync(string snapshot, CancellationToken ct = default)
        {
            if (_webView?.CoreWebView2 == null) return;

            _notified = false;

            var tcs = new TaskCompletionSource<(string text, string site)>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            // --- ① MutationObserver 登録 ---
            void OnMessage(object? s, CoreWebView2WebMessageReceivedEventArgs e)
            {
                if (_notified) return;
                try
                {
                    string raw = e.TryGetWebMessageAsString();
                    var doc = JsonSerializer.Deserialize<WebMessagePayload>(raw);
                    if (doc?.Type == "GenerationDone" && !string.IsNullOrWhiteSpace(doc.Text))
                        tcs.TrySetResult((doc.Text!, doc.Site ?? _adapter.SiteName));
                }
                catch { }
            }

            _webView.CoreWebView2.WebMessageReceived += OnMessage;
            ct.Register(() =>
            {
                _webView.CoreWebView2.WebMessageReceived -= OnMessage;
                tcs.TrySetCanceled();
            });

            await InjectObserverAsync(snapshot);

            // --- ② C# ポーリング（並列で動かす） ---
            _ = Task.Run(async () =>
            {
                try { await PollUntilDoneAsync(snapshot, tcs, ct); }
                catch (OperationCanceledException) { tcs.TrySetCanceled(); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            }, ct);

            // --- どちらか先に完了したら通知 ---
            try
            {
                var result = await tcs.Task;
                if (!_notified)
                {
                    _notified = true;
                    _webView.CoreWebView2.WebMessageReceived -= OnMessage;
                    GenerationDone?.Invoke(this,
                        new GenerationDoneEventArgs(result.site, result.text));
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                _webView.CoreWebView2.WebMessageReceived -= OnMessage;
            }
        }

        // ---------------------------------------------------------------
        // ポーリング本体
        // ---------------------------------------------------------------
        private async Task PollUntilDoneAsync(
            string snapshot,
            TaskCompletionSource<(string, string)> tcs,
            CancellationToken ct)
        {
            // フェーズ1: 生成が「開始」するのを待つ（最大 WaitForStartMs）
            int waitedMs = 0;
            while (!ct.IsCancellationRequested && waitedMs < WaitForStartMs)
            {
                bool generating = await _adapter.IsGeneratingAsync(_webView);
                string current  = await _adapter.ExtractLatestAsync(_webView);

                // 生成が始まった or テキストが変わった → フェーズ2へ
                if (generating || (!string.IsNullOrWhiteSpace(current) && current != snapshot))
                    break;

                await Task.Delay(PollIntervalMs, ct);
                waitedMs += PollIntervalMs;
            }

            if (ct.IsCancellationRequested) return;

            // フェーズ2: 生成が「完了」するのを待つ
            string stableText  = string.Empty;
            int    stableCount = 0;

            while (!ct.IsCancellationRequested)
            {
                bool   isGenerating = await _adapter.IsGeneratingAsync(_webView);
                string latestText   = await _adapter.ExtractLatestAsync(_webView);

                bool isNewText = !string.IsNullOrWhiteSpace(latestText) && latestText != snapshot;

                if (!isGenerating && isNewText)
                {
                    // テキストが安定しているかカウント
                    if (latestText == stableText)
                    {
                        stableCount++;
                        if (stableCount >= QuietStableCount)
                        {
                            // 完了確定
                            tcs.TrySetResult((latestText, _adapter.SiteName));
                            return;
                        }
                    }
                    else
                    {
                        stableText  = latestText;
                        stableCount = 1;
                    }
                }
                else if (isGenerating)
                {
                    // まだ生成中 → カウントリセット
                    stableCount = 0;
                }

                await Task.Delay(PollIntervalMs, ct);
            }
        }

        // ---------------------------------------------------------------
        // Observer 注入（補助。主役はポーリング）
        // ---------------------------------------------------------------
        private async Task InjectObserverAsync(string snapshot)
        {
            string escapedSnapshot = JsonSerializer.Serialize(snapshot);
            string siteName        = _adapter.SiteName;

            string latestTextExpr = siteName switch
            {
                "Gemini" =>
                    "Array.from(document.querySelectorAll('model-response .message-content')).pop()?.innerText?.trim() ?? ''",
                "Perplexity" =>
                    "(Array.from(document.querySelectorAll('.prose')).pop()?.innerText?.trim()) || " +
                    "(Array.from(document.querySelectorAll('[data-testid=\"answer\"]')).pop()?.innerText?.trim()) || ''",
                _ =>
                    "Array.from(document.querySelectorAll('.prose, model-response .message-content, [data-testid=\"answer\"]')).pop()?.innerText?.trim() ?? ''"
            };

            // シンプル版 Observer（isGenerating チェックなし・ポーリングに任せる）
            string script = $@"
(() => {{
    if (window.__gironObs) {{ window.__gironObs.disconnect(); window.__gironObs = null; }}
    window.__gironNotified = false;
    window.__gironTimer    = null;
    const SNAPSHOT = {escapedSnapshot};
    const QUIET_MS = 1800;

    const getLatest = () => {{ return {latestTextExpr}; }};

    const notify = (text) => {{
        if (window.__gironNotified) return;
        window.__gironNotified = true;
        window.__gironObs?.disconnect();
        try {{ chrome.webview.postMessage(JSON.stringify({{ type:'GenerationDone', text, site:'{siteName}' }})); }} catch(e) {{}}
    }};

    window.__gironObs = new MutationObserver(() => {{
        const t = getLatest();
        if (!t || t === SNAPSHOT) return;
        if (window.__gironTimer) clearTimeout(window.__gironTimer);
        window.__gironTimer = setTimeout(() => {{
            const ft = getLatest();
            if (ft && ft !== SNAPSHOT) notify(ft);
        }}, QUIET_MS);
    }});

    window.__gironObs.observe(document.body, {{ childList:true, subtree:true, characterData:true }});
}})();";

            await _webView.ExecuteScriptAsync(script);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }

        private sealed class WebMessagePayload
        {
            public string? Type { get; set; }
            public string? Text { get; set; }
            public string? Site { get; set; }
        }
    }

    public sealed class GenerationDoneEventArgs : EventArgs
    {
        public string SiteName { get; }
        public string Text     { get; }
        public GenerationDoneEventArgs(string siteName, string text)
        { SiteName = siteName; Text = text; }
    }
}
