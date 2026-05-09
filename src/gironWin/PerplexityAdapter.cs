using Microsoft.Web.WebView2.Wpf;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace gironWin
{
    public class PerplexityAdapter : BaseAiSiteAdapter
    {
        public override string SiteName => "Perplexity";

        public event EventHandler<string>? DebugLog;
        private void Log(string msg) => DebugLog?.Invoke(this, msg);

        public override bool CanHandle(string url)
        {
            return !string.IsNullOrWhiteSpace(url) &&
                   url.Contains("perplexity.ai", StringComparison.OrdinalIgnoreCase);
        }

        public override async Task<bool> SetInputAsync(WebView2 webView, string text)
        {
            if (webView?.CoreWebView2 == null) return false;

            // 診断: 入力欄を探す
            string diagScript = @"
(() => {
    const all = Array.from(document.querySelectorAll(
        'input,textarea,div[contenteditable],#ask-input'
    ));
    return JSON.stringify(all.map(el => ({
        tag: el.tagName,
        ce: el.getAttribute('contenteditable') || '',
        role: el.getAttribute('role') || '',
        id: el.id || '',
        cls: (el.className || '').substring(0, 60),
        vis: (() => {
            const s = window.getComputedStyle(el);
            return s.display !== 'none' && s.visibility !== 'hidden' && el.offsetParent !== null;
        })()
    })));
})();";
            try
            {
                string diagJson = await webView.ExecuteScriptAsync(diagScript);
                Log($"[PerplexityInput] DOM: {diagJson}");
            }
            catch (Exception ex) { Log($"[PerplexityInput] Diag error: {ex.Message}"); }

            string escapedText = JsonSerializer.Serialize(text);

            // ★ async/await/setTimeout を一切使わない同期スクリプト
            string script = $@"
(() => {{
    const text = {escapedText};

    function isVisible(el) {{
        if (!el) return false;
        const s = window.getComputedStyle(el);
        return s.display !== 'none' && s.visibility !== 'hidden' && el.offsetParent !== null;
    }}

    const selectors = [
        '#ask-input[contenteditable=""true""]',
        '#ask-input',
        'div[contenteditable=""true""][role=""textbox""]',
        'div[role=""textbox""][contenteditable=""true""]',
        'div[contenteditable=""true""]',
        'textarea',
        'input[type=""text""]'
    ];

    let el = null;
    for (const sel of selectors) {{
        const found = Array.from(document.querySelectorAll(sel)).filter(isVisible);
        if (found.length > 0) {{ el = found[0]; break; }}
    }}
    if (!el) return 'no-input';

    el.focus();

    // TEXTAREA / INPUT
    if (el.tagName === 'TEXTAREA' || el.tagName === 'INPUT') {{
        try {{
            const proto = Object.getPrototypeOf(el);
            const setter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;
            if (setter) setter.call(el, text); else el.value = text;
            el.dispatchEvent(new Event('input', {{ bubbles: true }}));
            el.dispatchEvent(new Event('change', {{ bubbles: true }}));
            return 'textarea-ok';
        }} catch(e) {{ return 'textarea-err:' + e.message; }}
    }}

    // contenteditable — まず全選択クリア
    try {{
        document.execCommand('selectAll', false, null);
        document.execCommand('delete', false, null);
    }} catch(e) {{}}

    // ① DataTransfer paste
    try {{
        const dt = new DataTransfer();
        dt.setData('text/plain', text);
        el.dispatchEvent(new ClipboardEvent('paste', {{
            bubbles: true, cancelable: true, clipboardData: dt
        }}));
        const cur = (el.innerText || el.textContent || '').trim();
        if (cur.length > 0) {{
            el.dispatchEvent(new InputEvent('input', {{
                bubbles: true, inputType: 'insertText', data: text
            }}));
            return 'paste-ok:' + cur.length;
        }}
    }} catch(e) {{}}

    // ② execCommand insertText
    try {{
        el.focus();
        document.execCommand('selectAll', false, null);
        const ok = document.execCommand('insertText', false, text);
        if (ok) {{
            el.dispatchEvent(new InputEvent('input', {{
                bubbles: true, inputType: 'insertText', data: text
            }}));
            return 'execCmd-ok';
        }}
    }} catch(e) {{}}

    // ③ 最終手段: innerText 直接代入 + React nativeInputValueSetter
    try {{
        el.innerHTML = '';
        const tn = document.createTextNode(text);
        el.appendChild(tn);
        el.dispatchEvent(new InputEvent('input', {{
            bubbles: true, inputType: 'insertText', data: text
        }}));
        return 'innerText-ok';
    }} catch(e) {{}}

    return 'all-failed';
}})();";

            string resultJson = await webView.ExecuteScriptAsync(script);
            string result = string.Empty;
            try { result = JsonSerializer.Deserialize<string>(resultJson) ?? string.Empty; }
            catch { result = resultJson?.Trim('"') ?? string.Empty; }

            Log($"[PerplexityInput] SetInputAsync result='{result}'");

            bool ok = !string.IsNullOrWhiteSpace(result)
                      && result != "no-input"
                      && result != "all-failed"
                      && !result.StartsWith("error")
                      && !result.EndsWith("-err");
            return ok;
        }

        public override async Task<bool> SendAsync(WebView2 webView)
        {
            if (webView?.CoreWebView2 == null) return false;

            // 診断: ボタンを列挙
            string diagScript = @"
(() => {
    const btns = Array.from(document.querySelectorAll('button')).slice(0, 30);
    return JSON.stringify(btns.map(b => ({
        al: b.getAttribute('aria-label') || '',
        id: b.id || '',
        cls: (b.className || '').substring(0, 50),
        dis: b.disabled,
        vis: (() => {
            const s = window.getComputedStyle(b);
            return s.display !== 'none' && s.visibility !== 'hidden';
        })()
    })));
})();";
            try
            {
                string diagJson = await webView.ExecuteScriptAsync(diagScript);
                Log($"[PerplexitySend] Buttons: {diagJson}");
            }
            catch { }

            string script = @"
(() => {
    function isEnabled(el) {
        if (!el) return false;
        if (el.disabled) return false;
        if (el.getAttribute('aria-disabled') === 'true') return false;
        const s = window.getComputedStyle(el);
        return s.display !== 'none' && s.visibility !== 'hidden';
    }

    const btnSelectors = [
        'button[aria-label=""Submit""]',
        'button[aria-label*=""Send""]',
        'button[aria-label*=""送信""]',
        'button#ask-submit',
        'button[data-testid=""submit-button""]',
        'button[type=""submit""]',
        'form button'
    ];

    for (const sel of btnSelectors) {
        const btns = Array.from(document.querySelectorAll(sel)).filter(isEnabled);
        if (btns.length > 0) {
            btns[0].focus();
            btns[0].click();
            return 'btn:' + sel;
        }
    }

    // Enter キーで送信
    const input = document.querySelector('#ask-input[contenteditable=""true""]')
        || document.querySelector('#ask-input')
        || document.querySelector('div[contenteditable=""true""][role=""textbox""]')
        || document.querySelector('div[contenteditable=""true""]')
        || document.querySelector('textarea');

    if (!input) return 'no-input';

    input.focus();
    const ev = { key: 'Enter', code: 'Enter', which: 13, keyCode: 13,
                 bubbles: true, cancelable: true };
    input.dispatchEvent(new KeyboardEvent('keydown', ev));
    input.dispatchEvent(new KeyboardEvent('keypress', ev));
    input.dispatchEvent(new KeyboardEvent('keyup', ev));
    return 'enter-key';
})();";

            string resultJson = await webView.ExecuteScriptAsync(script);
            string result = string.Empty;
            try { result = JsonSerializer.Deserialize<string>(resultJson) ?? string.Empty; }
            catch { result = resultJson?.Trim('"') ?? string.Empty; }

            Log($"[PerplexitySend] result='{result}'");
            return !string.IsNullOrWhiteSpace(result) && result != "no-input";
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
        const walker = document.createTreeWalker(
            root,
            NodeFilter.SHOW_TEXT,
            {
                acceptNode(node) {
                    if (!isVisible(node.parentElement)) return NodeFilter.FILTER_REJECT;
                    const t = (node.nodeValue || '').trim();
                    if (!t) return NodeFilter.FILTER_REJECT;
                    if (t.length <= 2 && /^\d+$/.test(t)) return NodeFilter.FILTER_REJECT;
                    return NodeFilter.FILTER_ACCEPT;
                }
            }
        );
        const parts = [];
        let n;
        while ((n = walker.nextNode())) {
            const val = (n.nodeValue || '').trim();
            if (!val) continue;
            const tag = (n.parentElement?.tagName || '').toLowerCase();
            const isBlock = ['p','h1','h2','h3','h4','h5','h6',
                             'li','blockquote','pre','div'].includes(tag);
            if (isBlock && parts.length > 0) parts.push('\n');
            parts.push(val);
        }
        return norm(parts.join(' ')
            .replace(/ \n /g, '\n')
            .replace(/\n +/g, '\n'));
    }

    const mdContainers = Array.from(
        document.querySelectorAll('div[id^=""markdown-content-""]')
    ).filter(isVisible);
    if (mdContainers.length > 0) {
        const text = extractFullText(mdContainers[mdContainers.length - 1]);
        if (text) return JSON.stringify(text);
    }

    const proseNodes = Array.from(document.querySelectorAll('.prose')).filter(isVisible);
    if (proseNodes.length > 0) {
        const texts = proseNodes.map(n => extractFullText(n)).filter(Boolean);
        if (texts.length > 0) return JSON.stringify(norm(texts.join('\n\n')));
    }

    const lmNodes = Array.from(document.querySelectorAll('[data-renderer=""lm""]')).filter(isVisible);
    if (lmNodes.length > 0) {
        const text = extractFullText(lmNodes[lmNodes.length - 1]);
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
    if (document.querySelector('button[aria-label=""Stop""], button[aria-label*=""Stop""]'))
        return true;
    if (document.querySelector('.animate-pulse,[data-generating=""true""],[aria-busy=""true""]'))
        return true;
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
