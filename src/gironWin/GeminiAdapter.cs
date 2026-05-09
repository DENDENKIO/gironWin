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
(async () => {{
    const text = {escapedText};

    function isVisible(el) {{
        if (!el) return false;
        const s = window.getComputedStyle(el);
        return s.display !== 'none' && s.visibility !== 'hidden' && el.offsetParent !== null;
    }}

    function findInput() {{
        const candidates = [
            'rich-textarea div[contenteditable=""true""]',
            'div[contenteditable=""true""][role=""textbox""]',
            'div[role=""textbox""][contenteditable=""true""]',
            'div[contenteditable=""true""]',
            'textarea'
        ];
        for (const sel of candidates) {{
            const nodes = Array.from(document.querySelectorAll(sel)).filter(isVisible);
            if (nodes.length > 0) return nodes[0];
        }}
        return null;
    }}

    async function insertIntoEditable(el, value) {{
        el.focus();
        try {{
            document.execCommand('selectAll', false, null);
            document.execCommand('delete', false, null);
        }} catch(e) {{}}

        // DataTransfer paste が最も確実
        try {{
            const dt = new DataTransfer();
            dt.setData('text/plain', value);
            el.dispatchEvent(new ClipboardEvent('paste', {{
                bubbles: true, cancelable: true, clipboardData: dt
            }}));
            await new Promise(r => setTimeout(r, 80));
            const cur = (el.innerText || el.textContent || '').trim();
            if (cur.length >= Math.min(value.length, 20)) {{
                el.dispatchEvent(new InputEvent('input', {{ bubbles: true, inputType: 'insertText', data: value }}));
                return true;
            }}
        }} catch(e) {{}}

        // フォールバック: execCommand
        try {{
            document.execCommand('selectAll', false, null);
            const ok = document.execCommand('insertText', false, value);
            if (ok) {{
                await new Promise(r => setTimeout(r, 50));
                el.dispatchEvent(new InputEvent('input', {{ bubbles: true, inputType: 'insertText', data: value }}));
                return true;
            }}
        }} catch(e) {{}}

        return false;
    }}

    const el = findInput();
    if (!el) return false;
    el.focus();
    await new Promise(r => setTimeout(r, 60));

    if (el.tagName === 'TEXTAREA' || el.tagName === 'INPUT') {{
        const proto = Object.getPrototypeOf(el);
        const setter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;
        if (setter) setter.call(el, text); else el.value = text;
        el.dispatchEvent(new Event('input', {{ bubbles: true }}));
        return true;
    }}
    return await insertIntoEditable(el, text);
}})();";

            return await ExecScriptBoolAsync(webView, script);
        }

        public override async Task<bool> SendAsync(WebView2 webView)
        {
            if (webView?.CoreWebView2 == null) return false;

            string script = @"
(() => {
    function isEnabled(el) {
        if (!el) return false;
        if (el.disabled) return false;
        if (el.getAttribute('aria-disabled') === 'true') return false;
        const s = window.getComputedStyle(el);
        return s.display !== 'none' && s.visibility !== 'hidden';
    }

    // 1) mat-icon 'send' を含む親 button
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
        'button[data-test-id*=""send"" i]'
    ];
    for (const sel of labelSelectors) {
        const buttons = Array.from(document.querySelectorAll(sel)).filter(isEnabled);
        for (const btn of buttons) { btn.click(); return true; }
    }

    // 3) フォーム内の有効な最後の button
    const formBtns = Array.from(document.querySelectorAll('form button')).filter(isEnabled);
    if (formBtns.length > 0) { formBtns[formBtns.length - 1].click(); return true; }

    // 4) Enter キーフォールバック
    const input = document.querySelector('rich-textarea div[contenteditable=""true""]')
        || document.querySelector('div[contenteditable=""true""][role=""textbox""]')
        || document.querySelector('div[contenteditable=""true""]')
        || document.querySelector('textarea');

    if (!input) return false;
    input.focus();
    ['keydown', 'keypress', 'keyup'].forEach(type => {
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
            // ★ JSON.stringify でラップして返す
            string script = @"
(() => {
    function isVisible(el) {
        if (!el) return false;
        const s = window.getComputedStyle(el);
        return s.display !== 'none' && s.visibility !== 'hidden';
    }

    function extractFullText(root) {
        if (!root) return '';
        const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
            acceptNode(node) {
                if (!isVisible(node.parentElement)) return NodeFilter.FILTER_REJECT;
                const t = (node.nodeValue || '').trim();
                if (!t) return NodeFilter.FILTER_REJECT;
                return NodeFilter.FILTER_ACCEPT;
            }
        });
        const parts = [];
        let n;
        while ((n = walker.nextNode())) {
            const val = (n.nodeValue || '').trim();
            if (!val) continue;
            const tag = n.parentElement?.tagName?.toLowerCase() || '';
            const isBlock = ['p','h1','h2','h3','h4','h5','h6','li','blockquote','pre','div'].includes(tag);
            if (isBlock && parts.length > 0) parts.push('\n');
            parts.push(val);
        }
        return parts.join(' ').replace(/ \n /g, '\n').replace(/\n +/g, '\n').trim();
    }

    const selectors = [
        'wide-model-response .message-content',
        'model-response .message-content',
        'model-response',
        '[data-response-index]',
        '[data-message-author-role=""model""]',
        '.response-container .markdown',
        '.markdown'
    ];

    for (const sel of selectors) {
        const nodes = Array.from(document.querySelectorAll(sel)).filter(isVisible);
        if (nodes.length > 0) {
            const text = extractFullText(nodes[nodes.length - 1]);
            if (text) return JSON.stringify(text);
        }
    }

    return JSON.stringify('');
})();";
            return await ExecScriptStringAsync(webView, script);
        }

        public override async Task<bool> IsGeneratingAsync(WebView2 webView)
        {
            string script = @"
(() => {
    const stopBtn = document.querySelector(
        'button[aria-label*=""Stop"" i], button[aria-label*=""停止""], button[aria-label*=""生成を停止""]'
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
