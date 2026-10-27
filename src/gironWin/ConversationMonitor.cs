using Microsoft.Web.WebView2.Wpf;
using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace gironWin
{
    public sealed class ConversationMonitor : IDisposable
    {
        public event EventHandler<GenerationDoneEventArgs>? GenerationDone;
        private readonly IAiSiteAdapter _adapter;
        private readonly WebView2 _webView;
        private const int PollIntervalMs = 300;
        private const int StableIdleMs = 5000;
        private bool _disposed;

        private enum GenerationState { Unknown, Generating, Idle }

        public ConversationMonitor(IAiSiteAdapter adapter, WebView2 webView)
        {
            _adapter = adapter;
            _webView = webView;
        }

        public async Task<string> WaitForCompletionAsync(
            string snapshot, int timeoutMs, CancellationToken ct = default)
        {
            string previous = (snapshot ?? string.Empty).Trim();
            string candidate = string.Empty;
            bool observedGeneration = false;
            bool warned = false;
            var elapsed = Stopwatch.StartNew();
            var stable = new Stopwatch();
            long nextDiagnosticMs = 10000;

            AppLogger.Info(LogCategory.Monitor,
                $"[{_adapter.SiteName}] Strict completion monitoring started; timeout is warning only.");

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(PollIntervalMs, ct);

                if (!warned && timeoutMs > 0 && elapsed.ElapsedMilliseconds >= timeoutMs)
                {
                    warned = true;
                    AppLogger.Warn(LogCategory.Monitor,
                        $"[{_adapter.SiteName}] Configured wait exceeded; still waiting, no partial transfer.");
                }

                GenerationState before = GenerationState.Unknown;
                GenerationState after = GenerationState.Unknown;
                string text = string.Empty;
                try
                {
                    before = await ReadStateAsync();
                    text = await _webView.Dispatcher.InvokeAsync(
                        async () => (await _adapter.ExtractLatestAsync(_webView))?.Trim()
                            ?? string.Empty).Task.Unwrap();
                    after = await ReadStateAsync();
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    stable.Reset();
                    candidate = string.Empty;
                    if (elapsed.ElapsedMilliseconds >= nextDiagnosticMs)
                    {
                        AppLogger.Warn(LogCategory.Monitor,
                            $"[{_adapter.SiteName}] Read failed; continuing without transfer: {ex.Message}");
                        nextDiagnosticMs = elapsed.ElapsedMilliseconds + 10000;
                    }
                    continue;
                }

                ct.ThrowIfCancellationRequested();
                if (before == GenerationState.Generating || after == GenerationState.Generating)
                    observedGeneration = true;

                bool eligible = observedGeneration
                    && before == GenerationState.Idle
                    && after == GenerationState.Idle
                    && !string.IsNullOrWhiteSpace(text)
                    && text != previous;

                if (!eligible)
                {
                    stable.Reset();
                    candidate = string.Empty;
                }
                else if (text != candidate)
                {
                    candidate = text;
                    stable.Restart();
                }
                else if (stable.ElapsedMilliseconds >= StableIdleMs)
                {
                    ct.ThrowIfCancellationRequested();
                    AppLogger.Info(LogCategory.Monitor,
                        $"[{_adapter.SiteName}] Observed generation followed by stable idle; complete len={text.Length}.");
                    GenerationDone?.Invoke(this, new GenerationDoneEventArgs(_adapter.SiteName, text));
                    return text;
                }

                if (elapsed.ElapsedMilliseconds >= nextDiagnosticMs)
                {
                    AppLogger.Debug(LogCategory.Monitor,
                        $"[{_adapter.SiteName}] Waiting: state={before}/{after}, observedGeneration={observedGeneration}, len={text.Length}, stableMs={stable.ElapsedMilliseconds}.");
                    nextDiagnosticMs = elapsed.ElapsedMilliseconds + 10000;
                }
            }
        }

        private async Task<GenerationState> ReadStateAsync()
        {
            string json = await _webView.Dispatcher.InvokeAsync(async () =>
            {
                if (_webView.CoreWebView2 == null) return "null";
                return await _webView.ExecuteScriptAsync(StateScript);
            }).Task.Unwrap();
            string state = JsonSerializer.Deserialize<string>(json) ?? "unknown";
            return state switch
            {
                "generating" => GenerationState.Generating,
                "idle" => GenerationState.Idle,
                _ => GenerationState.Unknown
            };
        }

        private const string StateScript = @"
(() => {
    const host = location.hostname.toLowerCase();
    const visible = el => {
        if (!el || !el.isConnected) return false;
        const s = getComputedStyle(el);
        return s.display !== 'none' && s.visibility !== 'hidden'
            && el.getClientRects().length > 0;
    };
    const buttons = Array.from(document.querySelectorAll('button')).filter(visible);
    const label = b => b.getAttribute('aria-label') || '';
    const iconIs = (b, name) => Array.from(b.querySelectorAll('mat-icon'))
        .some(i => i.getAttribute('fonticon') === name
            || i.getAttribute('data-mat-icon-name') === name
            || (i.textContent || '').trim().toLowerCase() === name);
    if (host === 'perplexity.ai' || host.endsWith('.perplexity.ai')) {
        if (buttons.some(b => label(b).includes('応答の生成を停止')
            || /\bstop\b/i.test(label(b))
            || Array.from(b.querySelectorAll('use')).some(u =>
                ((u.getAttribute('href') || u.getAttribute('xlink:href') || '')
                    .includes('pplx-icon-player-stop-filled'))))) return 'generating';
        if (document.querySelector('[data-generating=""true""], [aria-busy=""true""]'))
            return 'unknown';
        if (buttons.some(b => /^(送信|Send|Submit|Send message)$/i.test(label(b))))
            return 'idle';
        return 'unknown';
    }
    if (host === 'gemini.google.com') {
        if (buttons.some(b => label(b).includes('回答を停止')
            || label(b).includes('生成を停止') || /\bstop\b/i.test(label(b))
            || iconIs(b, 'stop') || iconIs(b, 'stop_circle')
            || b.closest('gem-icon-button.stop'))) return 'generating';
        if (document.querySelector('[data-loading=""true""], [aria-busy=""true""]'))
            return 'unknown';
        if (buttons.some(b => label(b).startsWith('音声入力')
            || /^(Microphone|Use microphone|Voice input)/i.test(label(b))
            || /^(送信|Send|Send message)$/i.test(label(b))
            || b.closest('gem-icon-button.speech_dictation_mic_button'))) return 'idle';
        return 'unknown';
    }
    return 'unknown';
})();";

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }

    public sealed class GenerationDoneEventArgs : EventArgs
    {
        public string SiteName { get; }
        public string Text { get; }
        public GenerationDoneEventArgs(string siteName, string text)
        {
            SiteName = siteName;
            Text = text;
        }
    }
}
