using Microsoft.Web.WebView2.Wpf;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace gironWin
{
    public class GeminiAdapter : BaseAiSiteAdapter
    {
        public override string SiteName => "Gemini";

        public override bool CanHandle(string url)
        {
            return !string.IsNullOrWhiteSpace(url) &&
                   (url.Contains("gemini.google.com", StringComparison.OrdinalIgnoreCase)
                 || url.Contains("aistudio.google.com", StringComparison.OrdinalIgnoreCase));
        }

        public override async Task<bool> SetInputAsync(WebView2 webView, string text)
        {
            if (webView?.CoreWebView2 == null) return false;

            string escapedText = JsonSerializer.Serialize(text);

            string script = $@"
(() => {{
    const text = {escapedText};

    const selectors = [
        'div[contenteditable=""true""][role=""textbox""]',
        'div[role=""textbox""][contenteditable=""true""]',
        'div[contenteditable=""true""]',
        'textarea',
        'input[type=""text""]',
        'input:not([type])'
    ];

    function isVisible(el) {{
        if (!el) return false;
        const s = window.getComputedStyle(el);
        return s.display !== 'none' && s.visibility !== 'hidden' && el.offsetParent !== null;
    }}

    function setValue(el, value) {{
        if (el.tagName === 'TEXTAREA' || el.tagName === 'INPUT') {{
            const proto = Object.getPrototypeOf(el);
            const setter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;
            if (setter) setter.call(el, value);
            else el.value = value;
            el.dispatchEvent(new Event('input', {{ bubbles: true }}));
            el.dispatchEvent(new Event('change', {{ bubbles: true }}));
            return true;
        }}

        if (el.isContentEditable || el.getAttribute('contenteditable') === 'true') {{
            el.focus();
            try {{
                document.execCommand('selectAll', false, null);
                document.execCommand('delete', false, null);
            }} catch(e) {{
                el.textContent = '';
            }}

            let inserted = false;
            try {{
                inserted = document.execCommand('insertText', false, value);
            }} catch(e) {{
                inserted = false;
            }}

            if (!inserted) el.textContent = value;

            el.dispatchEvent(new InputEvent('input', {{
                bubbles: true,
                inputType: 'insertText',
                data: value
            }}));
            el.dispatchEvent(new Event('change', {{ bubbles: true }}));
            return true;
        }}

        return false;
    }}

    for (const selector of selectors) {{
        const nodes = Array.from(document.querySelectorAll(selector)).filter(isVisible);
        for (const el of nodes) {{
            if (setValue(el, text)) return true;
        }}
    }}

    return false;
}})();";

            return await ExecScriptBoolAsync(webView, script);
        }

        public override async Task<bool> SendAsync(WebView2 webView)
        {
            if (webView?.CoreWebView2 == null) return false;

            // ★ Gemini の送信ボタンを幅広いセレクタで探す
            // mat-icon "send" を持つ button、または data属性、クラス名で探す
            string script = @"
(() => {
    function isEnabled(el) {
        if (!el) return false;
        if (el.disabled) return false;
        if (el.getAttribute('aria-disabled') === 'true') return false;
        const s = window.getComputedStyle(el);
        return s.display !== 'none' && s.visibility !== 'hidden';
    }

    // 1) mat-icon の 'send' を含む親 button
    const sendIcons = Array.from(document.querySelectorAll('mat-icon, .mat-icon'))
        .filter(el => (el.textContent || '').trim().toLowerCase() === 'send');
    for (const icon of sendIcons) {
        const btn = icon.closest('button');
        if (btn && isEnabled(btn)) { btn.click(); return true; }
    }

    // 2) aria-label に send/送信/submit を含む button
    const labelSelectors = [
        'button[aria-label*=""send"" i]',
        'button[aria-label*=""送信""]',
        'button[aria-label*=""submit"" i]',
        'button.send-button',
        'button[data-test-id*=""send"" i]',
        'button[jsname]'
    ];
    for (const sel of labelSelectors) {
        const buttons = Array.from(document.querySelectorAll(sel)).filter(isEnabled);
        for (const btn of buttons) { btn.click(); return true; }
    }

    // 3) フォーム内の最後の button (type=submit or 最後のボタン)
    const formBtns = Array.from(document.querySelectorAll('form button')).filter(isEnabled);
    if (formBtns.length > 0) { formBtns[formBtns.length - 1].click(); return true; }

    // 4) Enter キー (Shift なし)
    const input = document.querySelector('div[contenteditable=""true""][role=""textbox""]')
        || document.querySelector('rich-textarea div[contenteditable=""true""]')
        || document.querySelector('div[contenteditable=""true""]')
        || document.querySelector('textarea');

    if (!input) return false;
    input.focus();
    ['keydown','keypress','keyup'].forEach(type => {
        input.dispatchEvent(new KeyboardEvent(type, {
            bubbles: true, cancelable: true,
            key: 'Enter', code: 'Enter', keyCode: 13, which: 13
        }));
    });
    return true;
})();";

            return await ExecScriptBoolAsync(webView, script);
        }

        public override async Task<string> ExtractLatestAsync(WebView2 webView)
        {
            string script = @"
(() => {
    const selectors = [
        'model-response .message-content',
        'model-response',
        '[data-response-index]',
        '[data-message-author-role=""model""]',
        '.response-container .markdown',
        '.markdown'
    ];

    function isVisible(el) {
        if (!el) return false;
        const s = window.getComputedStyle(el);
        return s.display !== 'none' && s.visibility !== 'hidden';
    }

    for (const sel of selectors) {
        const nodes = Array.from(document.querySelectorAll(sel))
            .filter(isVisible)
            .map(x => (x.innerText || x.textContent || '').trim())
            .filter(x => x.length > 0);

        if (nodes.length > 0) {
            return nodes[nodes.length - 1];
        }
    }

    return '';
})();";
            return await ExecScriptStringAsync(webView, script);
        }

        public override async Task<bool> IsGeneratingAsync(WebView2 webView)
        {
            string script = @"
(() => {
    const stopBtn = document.querySelector(
        'button[aria-label*=""Stop""], button[aria-label*=""停止""], button[aria-label*=""生成を停止""]'
    );
    if (stopBtn) return true;

    const loading = document.querySelector(
        '.loading-container, .response-loading, thinking-block, [data-loading=""true""]'
    );
    if (loading) return true;

    return false;
})();";
            return await ExecScriptBoolAsync(webView, script);
        }

        public override async Task InjectObserverAsync(WebView2 webView)
        {
            await Task.CompletedTask;
        }
    }
}
