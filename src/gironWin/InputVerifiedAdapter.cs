using Microsoft.Web.WebView2.Wpf;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace gironWin
{
    // Input verification only. SendAsync still reports the underlying operation,
    // not server acceptance; receipt and completion monitoring are separate work.
    internal sealed class InputVerifiedAdapter : IAiSiteAdapter
    {
        private readonly IAiSiteAdapter _inner;
        private static readonly ConditionalWeakTable<WebView2, SemaphoreSlim> InputGates = new();
        private const int VerificationTimeoutMs = 4000;
        private const int StableMatchMs = 600;

        public InputVerifiedAdapter(IAiSiteAdapter inner)
            => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        public string SiteName => _inner.SiteName;
        public bool CanHandle(string url) => _inner.CanHandle(url);
        public Task<bool> SendAsync(WebView2 view) => _inner.SendAsync(view);
        public Task<string> GetSelectedTextAsync(WebView2 view) => _inner.GetSelectedTextAsync(view);
        public Task<string> ExtractLatestAsync(WebView2 view) => _inner.ExtractLatestAsync(view);
        public Task<bool> IsGeneratingAsync(WebView2 view) => _inner.IsGeneratingAsync(view);
        public Task InjectObserverAsync(WebView2 view) => _inner.InjectObserverAsync(view);

        public async Task<bool> SetInputAsync(WebView2 view, string text)
        {
            ArgumentNullException.ThrowIfNull(view);
            text ??= string.Empty;
            var gate = InputGates.GetValue(view, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            string token = JsonSerializer.Serialize(Guid.NewGuid().ToString("N"));
            try
            {
                var initial = await ProbeAsync(view, CaptureScript.Replace("__TOKEN__", token));
                if (!initial.Found)
                {
                    AppLogger.Warn(LogCategory.Transfer,
                        $"[InputVerify] {SiteName} no unambiguous supported editor; input blocked.");
                    return false;
                }

                bool reported = await view.Dispatcher.InvokeAsync(
                    () => _inner.SetInputAsync(view, text)).Task.Unwrap();
                if (!reported)
                {
                    AppLogger.Warn(LogCategory.Transfer,
                        $"[InputVerify] {SiteName} adapter rejected input; editor={initial.Identity}.");
                    return false;
                }

                var elapsed = Stopwatch.StartNew();
                var matched = new Stopwatch();
                var last = initial;
                string expected = Normalize(text, initial.TextControl);
                while (elapsed.ElapsedMilliseconds < VerificationTimeoutMs)
                {
                    last = await ProbeAsync(view, ReadScript.Replace("__TOKEN__", token));
                    if (!last.Found)
                    {
                        AppLogger.Warn(LogCategory.Transfer,
                            $"[InputVerify] {SiteName} captured editor detached or hidden; input blocked.");
                        return false;
                    }
                    string actual = Normalize(last.Text, last.TextControl);
                    if (string.Equals(expected, actual, StringComparison.Ordinal))
                    {
                        if (!matched.IsRunning) matched.Start();
                        if (matched.ElapsedMilliseconds >= StableMatchMs)
                        {
                            AppLogger.Debug(LogCategory.Transfer,
                                $"[InputVerify] {SiteName} match=True expectedLen={text.Length} actualLen={last.Text.Length} editor={last.Identity}.");
                            return true;
                        }
                    }
                    else matched.Reset();
                    await Task.Delay(100);
                }
                AppLogger.Warn(LogCategory.Transfer,
                    $"[InputVerify] {SiteName} match=False expectedLen={text.Length} actualLen={last.Text.Length} editor={last.Identity}; input blocked.");
                return false;
            }
            catch (Exception ex)
            {
                AppLogger.Warn(LogCategory.Transfer,
                    $"[InputVerify] {SiteName} verification failed; input blocked: {ex.Message}");
                return false;
            }
            finally
            {
                try
                {
                    await view.Dispatcher.InvokeAsync(async () =>
                    {
                        if (view.CoreWebView2 != null)
                            await view.ExecuteScriptAsync(
                                "delete window.__gironInputChecks?.[" + token + "];");
                    }).Task.Unwrap();
                }
                catch (Exception ex) { Debug.WriteLine(ex); }
                gate.Release();
            }
        }

        // Preserve spaces, quotes, backslashes and internal newlines.
        // contenteditable may supply terminal layout newlines; text controls do not.
        private static string Normalize(string value, bool textControl)
        {
            string result = value.Replace("\r\n", "\n").Replace('\r', '\n');
            return textControl ? result : result.TrimEnd('\n');
        }

        private static async Task<InputProbe> ProbeAsync(WebView2 view, string script)
        {
            string json = await view.Dispatcher.InvokeAsync(async () =>
            {
                if (view.CoreWebView2 == null) return "null";
                return await view.ExecuteScriptAsync(script);
            }).Task.Unwrap();
            return JsonSerializer.Deserialize<InputProbe>(json) ?? new InputProbe();
        }

        private sealed class InputProbe
        {
            public InputProbe() { }
            public bool Found { get; set; }
            public bool TextControl { get; set; }
            public string Text { get; set; } = string.Empty;
            public string Identity { get; set; } = string.Empty;
        }

        private const string CaptureScript = """
(() => {
    const usable = el => {
        if (!el || !el.isConnected || el.disabled || el.readOnly
            || el.getAttribute('aria-disabled') === 'true') return false;
        const style = getComputedStyle(el);
        return style.display !== 'none' && style.visibility !== 'hidden'
            && el.getClientRects().length > 0;
    };
    const gemini = location.hostname === 'gemini.google.com'
        || location.hostname === 'aistudio.google.com';
    const selectors = gemini ? [
        'rich-textarea div[contenteditable="true"]',
        'div[contenteditable="true"][role="textbox"]',
        'div[role="textbox"][contenteditable="true"]',
        '[contenteditable="true"]', 'textarea',
        'input[type="text"]', 'input:not([type])'
    ] : [
        '#ask-input[contenteditable="true"]', '#ask-input',
        'div[contenteditable="true"][role="textbox"]',
        'div[role="textbox"][contenteditable="true"]',
        'div[contenteditable="true"]', 'textarea', 'input[type="text"]'
    ];
    let editor = null;
    for (const selector of selectors) {
        const candidates = Array.from(document.querySelectorAll(selector)).filter(usable);
        if (candidates.length > 1) return { Found: false };
        if (candidates.length === 1) { editor = candidates[0]; break; }
    }
    if (!editor) return { Found: false };
    const control = editor.tagName === 'TEXTAREA' || editor.tagName === 'INPUT';
    if (!control && !editor.isContentEditable) return { Found: false };
    window.__gironInputChecks ||= Object.create(null);
    window.__gironInputChecks[__TOKEN__] = editor;
    return {
        Found: true, TextControl: control,
        Text: control ? editor.value : editor.innerText,
        Identity: editor.tagName + '#' + (editor.id || '')
            + '[role=' + (editor.getAttribute('role') || '') + ']'
    };
})();
""";

        private const string ReadScript = """
(() => {
    const editor = window.__gironInputChecks?.[__TOKEN__];
    if (!editor || !editor.isConnected || editor.disabled || editor.readOnly
        || editor.getAttribute('aria-disabled') === 'true') return { Found: false };
    const style = getComputedStyle(editor);
    if (style.display === 'none' || style.visibility === 'hidden'
        || editor.getClientRects().length === 0) return { Found: false };
    const control = editor.tagName === 'TEXTAREA' || editor.tagName === 'INPUT';
    if (!control && !editor.isContentEditable) return { Found: false };
    return {
        Found: true, TextControl: control,
        Text: control ? editor.value : editor.innerText,
        Identity: editor.tagName + '#' + (editor.id || '')
            + '[role=' + (editor.getAttribute('role') || '') + ']'
    };
})();
""";
    }
}
