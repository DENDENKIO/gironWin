using Microsoft.Web.WebView2.Wpf;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace gironWin
{
    // Receipt evidence is not evidence that the answer is complete.
    internal sealed class ReceiptVerifiedAdapter : IAiSiteAdapter
    {
        private readonly IAiSiteAdapter _inner;
        private static readonly ConditionalWeakTable<WebView2, SendSlot> Slots = new();
        private const int ReceiptTimeoutMs = 15000;

        public ReceiptVerifiedAdapter(IAiSiteAdapter inner)
            => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        public string SiteName => _inner.SiteName;
        public bool CanHandle(string url) => _inner.CanHandle(url);
        public Task<string> GetSelectedTextAsync(WebView2 view) => _inner.GetSelectedTextAsync(view);
        public Task<string> ExtractLatestAsync(WebView2 view) => _inner.ExtractLatestAsync(view);
        public Task<bool> IsGeneratingAsync(WebView2 view) => _inner.IsGeneratingAsync(view);
        public Task InjectObserverAsync(WebView2 view) => _inner.InjectObserverAsync(view);

        public async Task<bool> SetInputAsync(WebView2 view, string text)
        {
            ArgumentNullException.ThrowIfNull(view);
            var slot = Slots.GetValue(view, _ => new SendSlot());
            await slot.Gate.WaitAsync();
            try
            {
                var document = await ReadAsync(view, false, string.Empty);
                if (slot.Document != document.Token)
                {
                    slot.Document = document.Token;
                    slot.Pending = false;
                    slot.Expected = null;
                }
                if (slot.Pending) throw Uncertain("前回の送信受理が未確認です。");
                slot.Expected = null;
                var bound = await ReadAsync(view, true, text);
                if (!bound.EditorFound) return false;
                if (!await view.Dispatcher.InvokeAsync(
                    () => _inner.SetInputAsync(view, text)).Task.Unwrap()) return false;
                var after = await ReadAsync(view, false, text);
                if (after.Token != bound.Token || !after.EditorFound
                    || !SameInput(text, after)) return false;
                slot.Document = after.Token;
                slot.Expected = string.IsNullOrEmpty(text) ? null : text;
                return true;
            }
            finally { slot.Gate.Release(); }
        }

        public async Task<bool> SendAsync(WebView2 view)
        {
            ArgumentNullException.ThrowIfNull(view);
            var slot = Slots.GetValue(view, _ => new SendSlot());
            await slot.Gate.WaitAsync();
            try
            {
                if (slot.Pending) throw Uncertain("前回の送信受理が未確認です。");
                string expected = slot.Expected
                    ?? throw new InvalidOperationException("照合済みの送信本文がありません。再送は行いません。");
                var before = await ReadAsync(view, false, expected);
                if (before.Token != slot.Document || !before.EditorFound || !SameInput(expected, before))
                    throw new InvalidOperationException("送信直前に入力欄または本文が変わりました。送信を中止しました。");
                if (before.Generating)
                    throw new InvalidOperationException("送信先が生成中のため、送信を中止しました。");

                // Keep this set on every exception after the operation may have started.
                // Existing retry loops must not perform another click on uncertainty.
                slot.Pending = true;
                try
                {
                    bool operation = await view.Dispatcher.InvokeAsync(
                        () => _inner.SendAsync(view)).Task.Unwrap();
                    AppLogger.Debug(LogCategory.Transfer,
                        $"[SendReceipt] {SiteName} operationResult={operation}; checking receipt, no automatic resend.");
                    var elapsed = Stopwatch.StartNew();
                    while (elapsed.ElapsedMilliseconds < ReceiptTimeoutMs)
                    {
                        await Task.Delay(250);
                        var after = await ReadAsync(view, false, expected);
                        if (after.Token != before.Token)
                            throw Uncertain("受理確認中にページが切り替わりました。");
                        bool userAdded = after.MatchingUsers > before.MatchingUsers;
                        bool generationStarted = after.EditorFound
                            && string.IsNullOrWhiteSpace(after.EditorText) && after.Generating;
                        if (userAdded || generationStarted)
                        {
                            slot.Pending = false;
                            slot.Expected = null;
                            AppLogger.Info(LogCategory.Transfer,
                                $"[SendReceipt] {SiteName} accepted=True evidence={(userAdded ? "new-matching-user-message" : "editor-cleared-and-generation-started")}.");
                            return true;
                        }
                    }
                    throw Uncertain("15秒以内に送信受理を確認できませんでした。");
                }
                catch (SendReceiptUncertainException) { throw; }
                catch (Exception ex)
                {
                    throw Uncertain("送信操作または受理確認で例外が発生しました。", ex);
                }
            }
            finally { slot.Gate.Release(); }
        }

        private SendReceiptUncertainException Uncertain(string reason, Exception? inner = null)
        {
            string message = $"{SiteName}: {reason} 二重送信を防ぐため自動再送せず終了します。" +
                "送信先の会話を確認してください。再操作する場合は、会話の受理状況を確認してからページを再読み込みしてください。";
            AppLogger.Warn(LogCategory.Transfer, "[SendReceipt] " + message);
            return new SendReceiptUncertainException(message, inner);
        }

        private static bool SameInput(string expected, ReceiptProbe probe)
        {
            static string Normalize(string value)
                => value.Replace("\r\n", "\n").Replace('\r', '\n');
            string left = Normalize(expected);
            string right = Normalize(probe.EditorText);
            if (!probe.TextControl)
            {
                left = left.TrimEnd('\n');
                right = right.TrimEnd('\n');
            }
            return string.Equals(left, right, StringComparison.Ordinal);
        }

        private static async Task<ReceiptProbe> ReadAsync(WebView2 view, bool bind, string expected)
        {
            string script = ProbeScript.Replace("__BIND__", bind ? "true" : "false")
                .Replace("__EXPECTED__", JsonSerializer.Serialize(expected));
            string json = await view.Dispatcher.InvokeAsync(async () =>
            {
                if (view.CoreWebView2 == null)
                    throw new InvalidOperationException("WebView2が利用できません。");
                return await view.ExecuteScriptAsync(script);
            }).Task.Unwrap();
            var probe = JsonSerializer.Deserialize<ReceiptProbe>(json);
            if (probe == null || string.IsNullOrEmpty(probe.Token))
                throw new InvalidOperationException("送信受理確認用のページ状態を取得できません。");
            return probe;
        }

        private sealed class SendSlot
        {
            public readonly SemaphoreSlim Gate = new(1, 1);
            public string Document = string.Empty;
            public string? Expected;
            public bool Pending;
        }

        private sealed class ReceiptProbe
        {
            public ReceiptProbe() { }
            public string Token { get; set; } = string.Empty;
            public bool EditorFound { get; set; }
            public bool TextControl { get; set; }
            public string EditorText { get; set; } = string.Empty;
            public bool Generating { get; set; }
            public int MatchingUsers { get; set; }
        }

        private const string ProbeScript = """
(() => {
    const bind = __BIND__;
    const expected = __EXPECTED__;
    const visible = el => {
        if (!el || !el.isConnected) return false;
        const style = getComputedStyle(el);
        return style.display !== 'none' && style.visibility !== 'hidden'
            && el.getClientRects().length > 0;
    };
    const doc = window.__gironReceiptDocument ||= {
        token: (crypto.randomUUID ? crypto.randomUUID() : String(Date.now()) + ':' + Math.random()),
        editor: null
    };
    const gemini = location.hostname === 'gemini.google.com'
        || location.hostname === 'aistudio.google.com';
    if (bind) {
        doc.editor = null;
        const selectors = gemini ? [
            'rich-textarea div[contenteditable="true"]',
            'div[contenteditable="true"][role="textbox"]',
            'div[role="textbox"][contenteditable="true"]',
            '[contenteditable="true"]', 'textarea', 'input[type="text"]', 'input:not([type])'
        ] : [
            '#ask-input[contenteditable="true"]', '#ask-input',
            'div[contenteditable="true"][role="textbox"]',
            'div[role="textbox"][contenteditable="true"]',
            'div[contenteditable="true"]', 'textarea', 'input[type="text"]'
        ];
        for (const selector of selectors) {
            const nodes = Array.from(document.querySelectorAll(selector)).filter(el =>
                visible(el) && !el.disabled && !el.readOnly
                && el.getAttribute('aria-disabled') !== 'true');
            if (nodes.length > 1) break;
            if (nodes.length === 1) { doc.editor = nodes[0]; break; }
        }
    }
    const editor = doc.editor;
    const control = !!editor && (editor.tagName === 'TEXTAREA' || editor.tagName === 'INPUT');
    const editorFound = visible(editor) && (control || editor.isContentEditable);
    const buttons = Array.from(document.querySelectorAll('button')).filter(visible);
    const generating = buttons.some(button => {
        const label = button.getAttribute('aria-label') || '';
        if (label.includes('応答の生成を停止') || label.includes('回答を停止')
            || label.includes('生成を停止')
            || /^(Stop|Stop generating|Stop response|Stop responding)$/i.test(label)) return true;
        if (Array.from(button.querySelectorAll('use')).some(icon =>
            (icon.getAttribute('href') || icon.getAttribute('xlink:href') || '')
                .includes('pplx-icon-player-stop-filled'))) return true;
        return gemini && Array.from(button.querySelectorAll('mat-icon')).some(icon =>
            ['stop', 'stop_circle'].includes((icon.getAttribute('fonticon')
                || icon.getAttribute('data-mat-icon-name') || icon.textContent || '').trim().toLowerCase()));
    });
    const displayText = text => (text || '').replace(/\s+/g, ' ').trim();
    const userSelectors = gemini ? [
        'user-query .query-text', 'user-query', '[data-message-author-role="user"]'
    ] : [
        '[data-message-author-role="user"]', '[data-role="user"]', '[data-testid="user-message"]'
    ];
    let matching = 0;
    for (const selector of userSelectors) {
        const users = Array.from(document.querySelectorAll(selector)).filter(visible);
        if (users.length) {
            matching = users.filter(user => user !== editor && !user.contains(editor)
                && displayText(user.innerText) === displayText(expected)).length;
            break;
        }
    }
    return {
        Token: doc.token, EditorFound: !!editorFound, TextControl: control,
        EditorText: editorFound ? (control ? editor.value : editor.innerText) : '',
        Generating: generating, MatchingUsers: matching
    };
})();
""";
    }

    internal sealed class SendReceiptUncertainException : InvalidOperationException
    {
        public SendReceiptUncertainException(string message, Exception? inner = null)
            : base(message, inner) { }
    }
}
