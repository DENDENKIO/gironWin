using Microsoft.Web.WebView2.Wpf;
using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace gironWin
{
    public sealed class GenerationRequest
    {
        internal ConversationMonitor.Probe Baseline = new();
        internal string RawBaseline = string.Empty;
        internal string Id = string.Empty;
        internal bool Accepted;
        internal GenerationRequest() { }
    }

    public sealed class ConversationMonitor : IDisposable
    {
        private readonly IAiSiteAdapter _adapter;
        private readonly WebView2 _view;
        private bool _disposed;
        public event EventHandler<GenerationDoneEventArgs>? GenerationDone;

        public ConversationMonitor(IAiSiteAdapter adapter, WebView2 view)
        { _adapter = adapter; _view = view; }

        internal static async Task<GenerationRequest> PrepareAsync(
            IAiSiteAdapter adapter, WebView2 view, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            string raw = await view.Dispatcher.InvokeAsync(
                () => adapter.ExtractLatestAsync(view)).Task.Unwrap();
            string id = Guid.NewGuid().ToString("N");
            var baseline = await ReadAsync(view, "arm", id);
            ct.ThrowIfCancellationRequested();
            if (!baseline.Supported) throw new NotSupportedException("生成監視に対応していないページです。");
            return new GenerationRequest { Id = id, Baseline = baseline, RawBaseline = raw.Trim() };
        }

        public async Task<string> WaitForCompletionAsync(string snapshot, int timeoutMs,
            CancellationToken ct = default, GenerationRequest? request = null)
        {
            bool manual = request == null;
            request ??= await PrepareAsync(_adapter, _view, ct);
            if (manual && !string.IsNullOrEmpty(snapshot)) request.RawBaseline = snapshot.Trim();
            if (!manual && !request.Accepted)
                throw new InvalidOperationException("送信受理が未確認の回答は監視できません。");
            var elapsed = Stopwatch.StartNew();
            var stable = new Stopwatch();
            string candidate = string.Empty;
            long candidateVersion = -1;
            long diagnosticAt = 10000;
            AppLogger.Info(LogCategory.Monitor,
                $"[{_adapter.SiteName}] Monitoring request={request.Id} accepted={request.Accepted} manual={manual} timeoutMs={timeoutMs}.");
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (_disposed) throw new ObjectDisposedException(nameof(ConversationMonitor));
                if (timeoutMs > 0 && elapsed.ElapsedMilliseconds >= timeoutMs)
                    throw new TimeoutException($"{_adapter.SiteName}: 生成完了を{timeoutMs / 1000.0:F1}秒以内に確認できません。未完成文は転送しません。");
                await Task.Delay(300, ct);
                var before = await ReadAsync(_view, "read", request.Id);
                string text = (await _view.Dispatcher.InvokeAsync(
                    () => _adapter.ExtractLatestAsync(_view)).Task.Unwrap()).Trim();
                var after = await ReadAsync(_view, "read", request.Id);
                ct.ThrowIfCancellationRequested();
                if (before.Document != request.Baseline.Document || after.Document != request.Baseline.Document
                    || before.Request != request.Id || after.Request != request.Id)
                    throw new InvalidOperationException("回答監視中にページまたは監視対象の送信が変わりました。");
                bool changed = text != request.RawBaseline;
                bool newIdentity = after.AnswerId != request.Baseline.AnswerId;
                bool seen = before.SeenGenerating || after.SeenGenerating;
                bool fresh = manual ? seen && changed : request.Accepted &&
                    ((newIdentity && (changed || after.AnswerCount > request.Baseline.AnswerCount))
                        || (seen && changed));
                bool eligible = fresh && !string.IsNullOrWhiteSpace(text) && after.HasAnswer
                    && before.Idle && after.Idle && before.Version == after.Version;
                if (!eligible) { stable.Reset(); candidate = string.Empty; candidateVersion = -1; }
                else if (candidate != text || candidateVersion != after.Version)
                { candidate = text; candidateVersion = after.Version; stable.Restart(); }
                else if (stable.ElapsedMilliseconds >= 5000)
                {
                    AppLogger.Info(LogCategory.Monitor,
                        $"[{_adapter.SiteName}] Complete request={request.Id} len={text.Length} seenGeneration={seen}.");
                    GenerationDone?.Invoke(this, new GenerationDoneEventArgs(_adapter.SiteName, text));
                    return text;
                }
                if (elapsed.ElapsedMilliseconds >= diagnosticAt)
                {
                    AppLogger.Debug(LogCategory.Monitor,
                        $"[{_adapter.SiteName}] request={request.Id} accepted={request.Accepted} fresh={fresh} idle={after.Idle} seen={seen} len={text.Length} stableMs={stable.ElapsedMilliseconds}.");
                    diagnosticAt = elapsed.ElapsedMilliseconds + 10000;
                }
            }
        }

        private static async Task<Probe> ReadAsync(WebView2 view, string action, string request)
        {
            string script = StateScript.Replace("__ACTION__", JsonSerializer.Serialize(action))
                .Replace("__REQUEST__", JsonSerializer.Serialize(request));
            string json = await view.Dispatcher.InvokeAsync(async () =>
            {
                if (view.CoreWebView2 == null) throw new InvalidOperationException("WebView2が利用できません。");
                return await view.ExecuteScriptAsync(script);
            }).Task.Unwrap();
            var state = JsonSerializer.Deserialize<Probe>(json);
            if (state == null || string.IsNullOrEmpty(state.Document))
                throw new InvalidOperationException("生成状態を取得できません。");
            return state;
        }

        internal sealed class Probe
        {
            public Probe() { }
            public string Document { get; set; } = string.Empty;
            public string Request { get; set; } = string.Empty;
            public bool Supported { get; set; }
            public bool Idle { get; set; }
            public bool SeenGenerating { get; set; }
            public bool HasAnswer { get; set; }
            public string AnswerId { get; set; } = string.Empty;
            public int AnswerCount { get; set; }
            public long Version { get; set; }
        }

        private const string StateScript = """
(() => {
    const action = __ACTION__, request = __REQUEST__;
    const visible = el => {
        if (!el || !el.isConnected) return false;
        const s = getComputedStyle(el);
        return s.display !== 'none' && s.visibility !== 'hidden' && el.getClientRects().length > 0;
    };
    const host = location.hostname.toLowerCase();
    const pplx = host === 'perplexity.ai' || host.endsWith('.perplexity.ai');
    const gemini = host === 'gemini.google.com' || host === 'aistudio.google.com';
    let state = window.__gironCompletionTracker;
    if (!state) {
        state = window.__gironCompletionTracker = {
            document: crypto.randomUUID ? crypto.randomUUID() : String(Date.now()) + ':' + Math.random(),
            request: '', seen: false, ids: new WeakMap(), nextId: 1,
            version: 0, signature: '', scheduled: false
        };
        state.sample = () => {
            const selectors = pplx ? ['div[id^="markdown-content-"]', '.prose', '[data-renderer="lm"]']
                : ['wide-model-response .message-content', 'model-response .message-content',
                   'model-response', '[data-response-index]', '[data-message-author-role="model"]', '.response-container .markdown', '.markdown'];
            let answers = [];
            for (const selector of selectors) {
                answers = Array.from(document.querySelectorAll(selector)).filter(visible);
                if (answers.length) break;
            }
            const root = answers[answers.length - 1];
            let id = '';
            if (root) {
                if (!state.ids.has(root)) state.ids.set(root, String(state.nextId++));
                id = state.ids.get(root);
            }
            const body = root ? root.innerText || '' : '';
            const signature = id + ':' + body;
            if (signature !== state.signature) { state.version++; state.signature = signature; }
            const buttons = Array.from(document.querySelectorAll('button')).filter(visible);
            const label = button => button.getAttribute('aria-label') || '';
            const generating = buttons.some(button => {
                const text = label(button);
                if (text.includes('応答の生成を停止') || text.includes('回答を停止') || text.includes('生成を停止')
                    || /^(Stop|Stop generating|Stop response|Stop responding)$/i.test(text)) return true;
                if (pplx && Array.from(button.querySelectorAll('use')).some(icon =>
                    (icon.getAttribute('href') || icon.getAttribute('xlink:href') || '').includes('pplx-icon-player-stop-filled'))) return true;
                return gemini && Array.from(button.querySelectorAll('mat-icon')).some(icon =>
                    ['stop', 'stop_circle'].includes((icon.getAttribute('fonticon')
                        || icon.getAttribute('data-mat-icon-name') || icon.textContent || '').trim().toLowerCase()));
            });
            if (generating && state.request) state.seen = true;
            const busy = root && (root.matches('[aria-busy="true"], [data-loading="true"], [data-generating="true"]')
                || Array.from(root.querySelectorAll('[aria-busy="true"], [data-loading="true"], [data-generating="true"]')).some(visible));
            const ready = buttons.some(button => /^(送信|送信する|Send|Submit|Send message|Send a message)$/i.test(label(button))
                || (gemini && (label(button).startsWith('音声入力')
                    || /^(Microphone|Use microphone|Voice input)/i.test(label(button))
                    || button.closest('gem-icon-button.speech_dictation_mic_button'))));
            return { Document: state.document, Request: state.request, Supported: pplx || gemini,
                Idle: !!(ready && !generating && !busy), SeenGenerating: state.seen,
                HasAnswer: !!body.trim(), AnswerId: id, AnswerCount: answers.length, Version: state.version };
        };
        state.observer = new MutationObserver(() => {
            if (state.scheduled) return;
            state.scheduled = true;
            setTimeout(() => { state.scheduled = false; state.sample(); }, 50);
        });
        state.observer.observe(document.documentElement, { childList: true, subtree: true,
            characterData: true, attributes: true,
            attributeFilter: ['aria-label', 'aria-busy', 'data-loading', 'data-generating', 'class', 'style', 'hidden'] });
    }
    if (action === 'arm') { state.request = request; state.seen = false; }
    return state.sample();
})();
""";
        public void Dispose() => _disposed = true;
    }

    public sealed class GenerationDoneEventArgs : EventArgs
    {
        public string SiteName { get; }
        public string Text { get; }
        public GenerationDoneEventArgs(string siteName, string text) { SiteName = siteName; Text = text; }
    }
}
