using Microsoft.Web.WebView2.Wpf;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace gironWin
{
    public class GeminiAdapter : BaseAiSiteAdapter
    {
        public override string SiteName => "Gemini";

        public event EventHandler<string>? DebugLog;
        private void Log(string msg) => DebugLog?.Invoke(this, msg);

        public override bool CanHandle(string url)
        {
            return !string.IsNullOrWhiteSpace(url) &&
                   (url.Contains("gemini.google.com", StringComparison.OrdinalIgnoreCase)
                 || url.Contains("aistudio.google.com", StringComparison.OrdinalIgnoreCase));
        }

        public override async Task<bool> SetInputAsync(WebView2 webView, string text)
        {
            if (webView?.CoreWebView2 == null) return false;

            // ★ まず入力欄の診断
            string diagScript = @"
(() => {
    const all = Array.from(document.querySelectorAll(
        'input,textarea,div[contenteditable],rich-textarea'
    ));
    return JSON.stringify(all.map(el => ({
        tag: el.tagName,
        type: el.type || '',
        ce: el.getAttribute('contenteditable') || '',
        role: el.getAttribute('role') || '',
        id: el.id || '',
        className: (el.className || '').substring(0,60),
        visible: (function(e){
            const s=window.getComputedStyle(e);
            return s.display!=='none' && s.visibility!=='hidden' && e.offsetParent!==null;
        })(el)
    })));
})();";
            try
            {
                string diagJson = await webView.ExecuteScriptAsync(diagScript);
                Log($"[GeminiInput] DOM inputs: {diagJson}");
            }
            catch (Exception ex) { Log($"[GeminiInput] Diag error: {ex.Message}"); }

            string escapedText = JsonSerializer.Serialize(text);

            // ★ async を使わない同期スクリプトに変更（TaskCanceledException 防止）
            // DataTransfer paste → execCommand → textContent の順で試みる
            string script = $@"
(() => {{
    const text = {escapedText};

    function isVisible(el) {{
        if (!el) return false;
        const s = window.getComputedStyle(el);
        return s.display !== 'none' && s.visibility !== 'hidden' && el.offsetParent !== null;
    }}

    // 候補を幅広く収集
    const candidates = [
        ...Array.from(document.querySelectorAll('rich-textarea div[contenteditable=""true""]')),
        ...Array.from(document.querySelectorAll('div[contenteditable=""true""][role=""textbox""]')),
        ...Array.from(document.querySelectorAll('div[role=""textbox""][contenteditable=""true""]')),
        ...Array.from(document.querySelectorAll('[contenteditable=""true""]')),
        ...Array.from(document.querySelectorAll('textarea')),
        ...Array.from(document.querySelectorAll('input[type=""text""]')),
        ...Array.from(document.querySelectorAll('input:not([type])'))
    ].filter(isVisible);

    if (candidates.length === 0) return 'no-input';

    const el = candidates[0];
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
        }} catch(e) {{ return 'textarea-error:' + e.message; }}
    }}

    // contenteditable — DataTransfer paste
    try {{
        document.execCommand('selectAll', false, null);
        document.execCommand('delete', false, null);
    }} catch(e) {{}}

    try {{
        const dt = new DataTransfer();
        dt.setData('text/plain', text);
        const ok = el.dispatchEvent(new ClipboardEvent('paste', {{
            bubbles: true, cancelable: true, clipboardData: dt
        }}));
        const cur = (el.innerText || el.textContent || '').trim();
        if (cur.length >= Math.min(text.length, 5)) {{
            el.dispatchEvent(new InputEvent('input', {{ bubbles: true, inputType: 'insertText', data: text }}));
            return 'paste-ok:' + cur.length;
        }}
    }} catch(e) {{}}

    // execCommand('insertText')
    try {{
        el.focus();
        document.execCommand('selectAll', false, null);
        const r = document.execCommand('insertText', false, text);
        if (r) {{
            el.dispatchEvent(new InputEvent('input', {{ bubbles: true, inputType: 'insertText', data: text }}));
            return 'execCmd-ok';
        }}
    }} catch(e) {{}}

    // 直接代入（最終手段）
    try {{
        el.textContent = text;
        el.dispatchEvent(new InputEvent('input', {{ bubbles: true, inputType: 'insertText', data: text }}));
        return 'textContent-ok';
    }} catch(e) {{}}

    return 'all-failed';
}})();";

            string resultJson = await webView.ExecuteScriptAsync(script);
            string result = string.Empty;
            try { result = JsonSerializer.Deserialize<string>(resultJson) ?? string.Empty; }
            catch { result = resultJson?.Trim('"') ?? string.Empty; }

            Log($"[GeminiInput] SetInputAsync result='{result}'");

            bool success = !string.IsNullOrWhiteSpace(result)
                           && result != "no-input"
                           && result != "all-failed"
                           && !result.StartsWith("error");
            return success;
        }

        public override async Task<bool> SendAsync(WebView2 webView)
        {
            if (webView?.CoreWebView2 == null) return false;

            // 診断: ページ上の全ボタン
            string diagScript = @"
(() => {
    const btns = Array.from(document.querySelectorAll('button'));
    return JSON.stringify(btns.slice(0,20).map(b => ({
        disabled: b.disabled,
        ariaLabel: b.getAttribute('aria-label') || '',
        ariaDisabled: b.getAttribute('aria-disabled') || '',
        className: (b.className || '').substring(0,50),
        id: b.id || '',
        matIcon: (b.querySelector('mat-icon')?.textContent || '').trim(),
        svgTitle: (b.querySelector('title')?.textContent || '').trim(),
        visible: (function(el){
            const s=window.getComputedStyle(el);
            return s.display!=='none' && s.visibility!=='hidden';
        })(el)
    })));
})();";
            try
            {
                string diagJson = await webView.ExecuteScriptAsync(diagScript);
                Log($"[GeminiSend] Buttons: {diagJson}");
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

    // 1) mat-icon 'send'
    for (const icon of document.querySelectorAll('mat-icon,.mat-icon')) {
        if ((icon.textContent||'').trim().toLowerCase()==='send') {
            const btn = icon.closest('button');
            if (btn && isEnabled(btn)) { btn.click(); return 'mat-icon-send'; }
        }
    }

    // 2) svg に send を含む button
    for (const svg of document.querySelectorAll('button svg')) {
        if ((svg.innerHTML||'').toLowerCase().includes('send')) {
            const btn = svg.closest('button');
            if (btn && isEnabled(btn)) { btn.click(); return 'svg-send'; }
        }
    }

    // 3) aria-label バリエーション
    const ariaTests = [
        'button[aria-label=""Send message""]',
        'button[aria-label=""Send""]',
        'button[aria-label=""送信""]',
        'button[aria-label*=""send"" i]',
        'button[aria-label*=""submit"" i]',
        'button[aria-label*=""メッセージを送信""]'
    ];
    for (const sel of ariaTests) {
        const found = Array.from(document.querySelectorAll(sel)).filter(isEnabled);
        if (found.length > 0) { found[0].click(); return 'aria:' + sel; }
    }

    // 4) class/id/data属性
    const attrTests = [
        'button.send-button','button#send-button',
        'button[data-test-id=""send-button""]',
        'button[data-testid=""send-button""]',
        'button[jsaction*=""send"" i]'
    ];
    for (const sel of attrTests) {
        const found = Array.from(document.querySelectorAll(sel)).filter(isEnabled);
        if (found.length > 0) { found[0].click(); return 'attr:' + sel; }
    }

    // 5) フォーム内の最後の有効ボタン（type=submit または最後）
    const formBtns = Array.from(document.querySelectorAll('form button')).filter(isEnabled);
    if (formBtns.length > 0) {
        const submitBtns = formBtns.filter(b => b.type === 'submit');
        const target = submitBtns.length > 0 ? submitBtns[0] : formBtns[formBtns.length - 1];
        target.click();
        return 'form-btn:class=' + target.className.substring(0,30);
    }

    // 6) Enter キーフォールバック
    const input = document.querySelector('rich-textarea div[contenteditable=""true""]')
        || document.querySelector('div[contenteditable=""true""][role=""textbox""]')
        || document.querySelector('div[contenteditable=""true""]')
        || document.querySelector('textarea');

    if (!input) return 'no-input-found';
    input.focus();
    ['keydown','keypress','keyup'].forEach(type => {
        input.dispatchEvent(new KeyboardEvent(type, {
            bubbles: true, cancelable: true,
            key: 'Enter', code: 'Enter', keyCode: 13, which: 13
        }));
    });
    return 'enter-key';
})();";

            string resultJson = await webView.ExecuteScriptAsync(script);
            string result = string.Empty;
            try { result = JsonSerializer.Deserialize<string>(resultJson) ?? string.Empty; }
            catch { result = resultJson?.Trim('"') ?? string.Empty; }

            Log($"[GeminiSend] SendAsync result='{result}'");

            return !string.IsNullOrWhiteSpace(result)
                   && result != "no-input-found"
                   && result != "false";
        }

        public override async Task<string> ExtractLatestAsync(WebView2 webView)
        {
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
        'button[aria-label*=""Stop"" i],button[aria-label*=""停止""],button[aria-label*=""生成を停止""]'
    );
    if (stopBtn) return true;
    const loading = document.querySelector(
        '.loading-container,.response-loading,thinking-block,[data-loading=""true""]'
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
