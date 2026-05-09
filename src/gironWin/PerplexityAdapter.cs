using Microsoft.Web.WebView2.Wpf;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace gironWin
{
    public class PerplexityAdapter : BaseAiSiteAdapter
    {
        public override string SiteName => "Perplexity";

        public override bool CanHandle(string url)
        {
            return !string.IsNullOrWhiteSpace(url) &&
                   url.Contains("perplexity.ai", StringComparison.OrdinalIgnoreCase);
        }

        public override async Task<bool> SetInputAsync(WebView2 webView, string text)
        {
            if (webView?.CoreWebView2 == null) return false;

            string escapedText = JsonSerializer.Serialize(text);

            string script = $@"
(() => {{
    const text = {escapedText};

    const selectors = [
        '#ask-input[contenteditable=""true""]',
        '#ask-input',
        'div[contenteditable=""true""][role=""textbox""]',
        'div[role=""textbox""][contenteditable=""true""]',
        'div[contenteditable=""true""]',
        'textarea'
    ];

    function isVisible(el) {{
        if (!el) return false;
        const s = window.getComputedStyle(el);
        return s.display !== 'none' && s.visibility !== 'hidden' && el.offsetParent !== null;
    }}

    function setEditable(el, value) {{
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

    function setControl(el, value) {{
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
            return setEditable(el, value);
        }}

        return false;
    }}

    for (const sel of selectors) {{
        const nodes = Array.from(document.querySelectorAll(sel)).filter(isVisible);
        for (const el of nodes) {{
            if (setControl(el, text)) return true;
        }}
    }}

    return false;
}})();";

            return await ExecScriptBoolAsync(webView, script);
        }

        public override async Task<bool> SendAsync(WebView2 webView)
        {
            if (webView?.CoreWebView2 == null) return false;

            string script = @"
(() => {
    function isVisible(el) {
        if (!el) return false;
        const s = window.getComputedStyle(el);
        return s.display !== 'none' && s.visibility !== 'hidden' && el.offsetParent !== null;
    }

    const buttonSelectors = [
        'button[aria-label=""Submit""]',
        'button[aria-label*=""Send""]',
        'button[aria-label*=""送信""]',
        'button[type=""submit""]',
        'form button'
    ];

    for (const sel of buttonSelectors) {
        const buttons = Array.from(document.querySelectorAll(sel)).filter(isVisible);
        for (const btn of buttons) {
            if (!btn.disabled) {
                btn.focus();
                btn.click();
                return true;
            }
        }
    }

    const input = document.querySelector('#ask-input[contenteditable=""true""]')
        || document.querySelector('#ask-input')
        || document.querySelector('div[contenteditable=""true""][role=""textbox""]')
        || document.querySelector('div[contenteditable=""true""]')
        || document.querySelector('textarea');

    if (!input) return false;

    input.focus();

    const ev = {
        key: 'Enter',
        code: 'Enter',
        which: 13,
        keyCode: 13,
        bubbles: true,
        cancelable: true
    };

    input.dispatchEvent(new KeyboardEvent('keydown', ev));
    input.dispatchEvent(new KeyboardEvent('keypress', ev));
    input.dispatchEvent(new KeyboardEvent('keyup', ev));

    return true;
})();";

            return await ExecScriptBoolAsync(webView, script);
        }

        public override async Task<string> ExtractLatestAsync(WebView2 webView)
        {
            string script = @"
(() => {
    function norm(text) {
        return (text || '')
            .replace(/\r/g, '')
            .replace(/\n{3,}/g, '\n\n')
            .trim();
    }

    function isVisible(el) {
        if (!el) return false;
        const s = window.getComputedStyle(el);
        return s.display !== 'none' && s.visibility !== 'hidden';
    }

    function collectTexts(root) {
        if (!root) return '';

        const blockSelectors = [
            '.prose',
            '.markdown',
            '[data-testid=""answer""]',
            '[data-testid=""response""]',
            'p', 'li', 'h1', 'h2', 'h3', 'h4', 'pre', 'code', 'blockquote'
        ];

        const seen = new Set();
        const parts = [];

        for (const sel of blockSelectors) {
            const nodes = Array.from(root.querySelectorAll(sel))
                .filter(isVisible);

            for (const node of nodes) {
                const text = norm(node.innerText || node.textContent || '');
                if (!text) continue;
                if (seen.has(text)) continue;
                seen.add(text);
                parts.push(text);
            }
        }

        if (parts.length > 0) {
            return norm(parts.join('\n\n'));
        }

        return norm(root.innerText || root.textContent || '');
    }

    // まず「回答全体コンテナ」を広めに探す
    const answerContainers = [
        ...Array.from(document.querySelectorAll('[data-testid=""answer""]')),
        ...Array.from(document.querySelectorAll('[data-testid=""response""]')),
        ...Array.from(document.querySelectorAll('main .prose')).map(x => x.closest('article, div')),
        ...Array.from(document.querySelectorAll('.prose')).map(x => x.closest('article, div'))
    ].filter(Boolean);

    // 重複除去
    const uniqueContainers = [];
    const containerSet = new Set();
    for (const c of answerContainers) {
        if (!containerSet.has(c)) {
            containerSet.add(c);
            uniqueContainers.push(c);
        }
    }

    // 後ろから見て、一番新しいコンテナの全文を作る
    for (let i = uniqueContainers.length - 1; i >= 0; i--) {
        const container = uniqueContainers[i];
        const text = collectTexts(container);
        if (text) return text;
    }

    // フォールバック: 画面上の prose 群を全部つなぐ
    const proseTexts = Array.from(document.querySelectorAll('.prose'))
        .filter(isVisible)
        .map(x => norm(x.innerText || x.textContent || ''))
        .filter(Boolean);

    if (proseTexts.length > 0) {
        return norm(proseTexts.join('\n\n'));
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
        'button[aria-label=""Stop""], button[aria-label*=""Stop""]'
    );
    if (stopBtn) return true;

    const loading = document.querySelector(
        '.animate-pulse, [data-generating=""true""], [aria-busy=""true""]'
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
