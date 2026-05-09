using Microsoft.Web.WebView2.Wpf;
using System.Text.Json;
using System.Threading.Tasks;

namespace gironWin
{
    public class GeminiAdapter : BaseAiSiteAdapter
    {
        public override string SiteName => "Gemini";

        public override bool CanHandle(string url) =>
            !string.IsNullOrWhiteSpace(url) &&
            url.Contains("gemini.google.com", System.StringComparison.OrdinalIgnoreCase);

        // ---------------------------------------------------------------
        // 入力
        // ---------------------------------------------------------------
        public override async Task<bool> SetInputAsync(WebView2 webView, string text)
        {
            string escapedText = JsonSerializer.Serialize(text);
            string script = $@"
(() => {{
    const text = {escapedText};
    const selectors = [
        'div.ql-editor[contenteditable=""true""]',
        'div[role=""textbox""][contenteditable=""true""]',
        '[contenteditable=""true""]',
        '[role=""textbox""]',
        'textarea'
    ];
    function placeCaretAtEnd(el) {{
        try {{
            const r = document.createRange();
            r.selectNodeContents(el);
            r.collapse(false);
            const s = window.getSelection();
            s.removeAllRanges();
            s.addRange(r);
        }} catch(e) {{}}
    }}
    for (const sel of selectors) {{
        for (const el of Array.from(document.querySelectorAll(sel))) {{
            const style = window.getComputedStyle(el);
            if (style.display === 'none' || style.visibility === 'hidden') continue;
            el.focus();
            if (el.tagName === 'TEXTAREA' || el.tagName === 'INPUT') {{
                const proto = Object.getPrototypeOf(el);
                const vs = Object.getOwnPropertyDescriptor(proto, 'value')?.set;
                if (vs) vs.call(el, text); else el.value = text;
                el.dispatchEvent(new Event('input', {{ bubbles: true }}));
                el.dispatchEvent(new Event('change', {{ bubbles: true }}));
                return true;
            }}
            if (el.isContentEditable || el.getAttribute('contenteditable') === 'true' || el.getAttribute('role') === 'textbox') {{
                el.textContent = text;
                placeCaretAtEnd(el);
                el.dispatchEvent(new Event('input', {{ bubbles: true }}));
                el.dispatchEvent(new Event('change', {{ bubbles: true }}));
                return true;
            }}
        }}
    }}
    return false;
}})();";
            return await ExecScriptBoolAsync(webView, script);
        }

        // ---------------------------------------------------------------
        // 送信
        // ---------------------------------------------------------------
        public override async Task<bool> SendAsync(WebView2 webView)
        {
            string script = @"
(() => {
    const btnSelectors = [
        'button[aria-label*=""Send""]',
        'button[aria-label*=""送信""]',
        'button[aria-label*=""プロンプトを送信""]',
        'button[data-test-id=""send-button""]'
    ];
    for (const sel of btnSelectors) {
        const btn = document.querySelector(sel);
        if (btn && !btn.disabled) { btn.click(); return true; }
    }
    const el = document.querySelector('div.ql-editor[contenteditable=""true""]')
        || document.querySelector('[role=""textbox""]')
        || document.querySelector('textarea');
    if (!el) return false;
    el.focus();
    el.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, cancelable: true, key: 'Enter', code: 'Enter' }));
    el.dispatchEvent(new KeyboardEvent('keyup',   { bubbles: true, cancelable: true, key: 'Enter', code: 'Enter' }));
    return true;
})();";
            return await ExecScriptBoolAsync(webView, script);
        }

        // ---------------------------------------------------------------
        // Phase 2: 最新メッセージ取得
        // ---------------------------------------------------------------
        public override async Task<string> ExtractLatestAsync(WebView2 webView)
        {
            string script = @"
(() => {
    const selectors = [
        'model-response .message-content',
        'model-response',
        '[data-response-index]',
        '.response-container .markdown',
        '.markdown'
    ];

    for (const sel of selectors) {
        const nodes = Array.from(document.querySelectorAll(sel))
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

        // ---------------------------------------------------------------
        // Phase 2: 生成中判定
        // ---------------------------------------------------------------
        public override async Task<bool> IsGeneratingAsync(WebView2 webView)
        {
            // 送信ボタンが有効 = 生成完了、無効 = 生成中
            string script = @"
(() => {
    // 送信ボタンが disabled のとき生成中
    const sendBtns = document.querySelectorAll(
        'button[aria-label*=""Send""], button[aria-label*=""送信""], button[aria-label*=""プロンプトを送信""]'
    );
    for (const btn of sendBtns) {
        if (btn.disabled) return true;
    }
    // Stop ボタンが表示されているとき生成中
    const stopBtn = document.querySelector(
        'button[aria-label*=""Stop""], button[aria-label*=""停止""], button[aria-label*=""生成を停止""]'
    );
    if (stopBtn) return true;
    // loading-container や thinking が表示されているとき
    const thinking = document.querySelector(
        '.loading-container, thinking-block, .response-loading'
    );
    if (thinking) return true;
    return false;
})();";
            return await ExecScriptBoolAsync(webView, script);
        }

        // ---------------------------------------------------------------
        // Phase 2: MutationObserver 注入
        // ---------------------------------------------------------------
        public override async Task InjectObserverAsync(WebView2 webView)
        {
            if (webView?.CoreWebView2 == null) return;

            string script = @"
(() => {
    if (window.__gironObserverActive) return;
    window.__gironObserverActive = true;
    window.__gironLastText = '';
    window.__gironQuietTimer = null;
    const QUIET_MS = 1800;

    function getLatestText() {
        const responses = Array.from(document.querySelectorAll('model-response .message-content'));
        if (responses.length > 0) return responses[responses.length - 1].innerText?.trim() ?? '';
        const fallback = Array.from(document.querySelectorAll('[data-response-index]'));
        if (fallback.length > 0) return fallback[fallback.length - 1].innerText?.trim() ?? '';
        return '';
    }

    function notifyDone(text) {
        window.__gironObserverActive = false;
        chrome.webview.postMessage(JSON.stringify({ type: 'GenerationDone', text: text, site: 'Gemini' }));
    }

    const observer = new MutationObserver(() => {
        const text = getLatestText();
        if (text === window.__gironLastText) return;
        window.__gironLastText = text;
        if (window.__gironQuietTimer) clearTimeout(window.__gironQuietTimer);
        window.__gironQuietTimer = setTimeout(() => {
            const sendBtn = document.querySelector('button[aria-label*=""Send""], button[aria-label*=""送信""]');
            if (sendBtn && !sendBtn.disabled) notifyDone(text);
        }, QUIET_MS);
    });

    observer.observe(document.body, { childList: true, subtree: true, characterData: true });
})();";
            await webView.ExecuteScriptAsync(script);
        }
    }
}
