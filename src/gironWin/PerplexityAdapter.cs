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
    const ev = { key: 'Enter', code: 'Enter', which: 13, keyCode: 13, bubbles: true, cancelable: true };
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

    function extractFullText(root) {
        if (!root) return '';

        // TreeWalkerで全テキストノードを走査（サブツリーを重複なく順に取得）
        const walker = document.createTreeWalker(
            root,
            NodeFilter.SHOW_TEXT,
            {
                acceptNode(node) {
                    // 非表示要素内のテキストは除外
                    if (!isVisible(node.parentElement)) return NodeFilter.FILTER_REJECT;
                    const t = (node.nodeValue || '').trim();
                    if (!t) return NodeFilter.FILTER_REJECT;
                    // citation系の数字やドメイン名（短い断片）は除外
                    if (t.length <= 2 && /^\d+$/.test(t)) return NodeFilter.FILTER_REJECT;
                    return NodeFilter.FILTER_ACCEPT;
                }
            }
        );

        const parts = [];
        let prev = '';
        let n;
        while ((n = walker.nextNode())) {
            const val = (n.nodeValue || '').trim();
            if (!val) continue;

            // ブロック要素の後なら改行を入れる
            const parent = n.parentElement;
            const tag = parent?.tagName?.toLowerCase() || '';
            const isBlock = ['p','h1','h2','h3','h4','h5','h6','li','blockquote','pre','div'].includes(tag);

            if (isBlock && parts.length > 0 && prev !== '\n') {
                parts.push('\n');
            }
            parts.push(val);
            prev = val;
        }

        return norm(parts.join(' ')
            .replace(/ \n /g, '\n')
            .replace(/\n +/g, '\n'));
    }

    // 最新の markdown-content-* を取得
    const mdContainers = Array.from(
        document.querySelectorAll('div[id^=""markdown-content-""]')
    ).filter(isVisible);

    if (mdContainers.length > 0) {
        // 最後のコンテナ = 最新の回答
        const latest = mdContainers[mdContainers.length - 1];
        const text = extractFullText(latest);
        if (text) return text;
    }

    // フォールバック1: .prose を最後から試す
    const proseNodes = Array.from(document.querySelectorAll('.prose'))
        .filter(isVisible);
    if (proseNodes.length > 0) {
        const texts = proseNodes.map(n => extractFullText(n)).filter(Boolean);
        if (texts.length > 0) return norm(texts.join('\n\n'));
    }

    // フォールバック2: data-renderer=lm を探す
    const lmNodes = Array.from(document.querySelectorAll('[data-renderer=""lm""]'))
        .filter(isVisible);
    if (lmNodes.length > 0) {
        const latest = lmNodes[lmNodes.length - 1];
        const text = extractFullText(latest);
        if (text) return text;
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
