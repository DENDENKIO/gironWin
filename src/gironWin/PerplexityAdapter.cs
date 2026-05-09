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

            // ★ 修正版:
            //   1. DataTransfer API（clipboard write 相当）で insertText
            //   2. 失敗したら execCommand('insertText') にフォールバック
            //   3. textContent への直接代入は使わない（Reactがリセットするため）
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
            '#ask-input[contenteditable=""true""]',
            '#ask-input',
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

    // ---- contenteditable への確実な挿入 ----
    async function insertIntoEditable(el, value) {{
        el.focus();

        // 全選択して削除
        try {{
            document.execCommand('selectAll', false, null);
            document.execCommand('delete', false, null);
        }} catch(e) {{}}

        // DataTransfer を使った貼り付け（最も確実）
        try {{
            const dt = new DataTransfer();
            dt.setData('text/plain', value);
            const pasteEvent = new ClipboardEvent('paste', {{
                bubbles: true,
                cancelable: true,
                clipboardData: dt
            }});
            el.dispatchEvent(pasteEvent);

            // 貼り付け後に内容を確認
            await new Promise(r => setTimeout(r, 80));
            const current = (el.innerText || el.textContent || '').trim();
            if (current.length >= Math.min(value.length, 20)) {{
                el.dispatchEvent(new InputEvent('input', {{ bubbles: true, inputType: 'insertText', data: value }}));
                return true;
            }}
        }} catch(e) {{}}

        // フォールバック: execCommand('insertText')
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

    function insertIntoTextarea(el, value) {{
        const proto = Object.getPrototypeOf(el);
        const setter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;
        if (setter) setter.call(el, value);
        else el.value = value;
        el.dispatchEvent(new Event('input', {{ bubbles: true }}));
        el.dispatchEvent(new Event('change', {{ bubbles: true }}));
        return true;
    }}

    const el = findInput();
    if (!el) return false;

    el.focus();
    await new Promise(r => setTimeout(r, 60));

    if (el.tagName === 'TEXTAREA' || el.tagName === 'INPUT') {{
        return insertIntoTextarea(el, text);
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
        if (text) return JSON.stringify(text);
    }

    // フォールバック1: .prose を最後から試す
    const proseNodes = Array.from(document.querySelectorAll('.prose'))
        .filter(isVisible);
    if (proseNodes.length > 0) {
        const texts = proseNodes.map(n => extractFullText(n)).filter(Boolean);
        if (texts.length > 0) return JSON.stringify(norm(texts.join('\n\n')));
    }

    // フォールバック2: data-renderer=lm を探す
    const lmNodes = Array.from(document.querySelectorAll('[data-renderer=""lm""]'))
        .filter(isVisible);
    if (lmNodes.length > 0) {
        const latest = lmNodes[lmNodes.length - 1];
        const text = extractFullText(latest);
        if (text) return JSON.stringify(text);
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
