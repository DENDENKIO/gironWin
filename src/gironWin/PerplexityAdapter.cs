using Microsoft.Web.WebView2.Wpf;
using System.Text.Json;
using System.Threading.Tasks;

namespace gironWin
{
    public class PerplexityAdapter : BaseAiSiteAdapter
    {
        public override string SiteName => "Perplexity";

        public override bool CanHandle(string url) =>
            !string.IsNullOrWhiteSpace(url) &&
            url.Contains("perplexity.ai", System.StringComparison.OrdinalIgnoreCase);

        // ---------------------------------------------------------------
        // 入力
        // ---------------------------------------------------------------
        public override async Task<bool> SetInputAsync(WebView2 webView, string text)
        {
            string escapedText = JsonSerializer.Serialize(text);
            string script = $@"
(() => {{
    const text = {escapedText};
    const el = document.querySelector('#ask-input[contenteditable=""true""][role=""textbox""]')
        || document.querySelector('#ask-input')
        || document.querySelector('[contenteditable=""true""]#ask-input');
    if (!el) return false;
    el.focus();
    try {{
        document.execCommand('selectAll', false, null);
        document.execCommand('delete', false, null);
    }} catch(e) {{ el.textContent = ''; }}
    let inserted = false;
    try {{ inserted = document.execCommand('insertText', false, text); }} catch(e) {{}}
    if (!inserted) el.textContent = text;
    el.dispatchEvent(new InputEvent('input', {{ bubbles: true, inputType: 'insertText', data: text }}));
    el.dispatchEvent(new Event('change', {{ bubbles: true }}));
    return true;
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
    const el = document.querySelector('#ask-input[contenteditable=""true""][role=""textbox""]')
        || document.querySelector('#ask-input');
    if (!el) return false;
    el.focus();
    ['keydown','keypress','keyup'].forEach(type => {
        el.dispatchEvent(new KeyboardEvent(type, {
            key: 'Enter', code: 'Enter', which: 13, keyCode: 13,
            bubbles: true, cancelable: true
        }));
    });
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
    // Perplexity の AI 応答は prose クラスの div に格納される
    const msgs = Array.from(document.querySelectorAll('.prose'));
    if (msgs.length > 0) return msgs[msgs.length - 1].innerText?.trim() ?? '';
    // フォールバック
    const fallback = Array.from(document.querySelectorAll('[data-testid=""answer""]'));
    if (fallback.length > 0) return fallback[fallback.length - 1].innerText?.trim() ?? '';
    return '';
})();";
            return await ExecScriptStringAsync(webView, script);
        }

        // ---------------------------------------------------------------
        // Phase 2: 生成中判定
        // ---------------------------------------------------------------
        public override async Task<bool> IsGeneratingAsync(WebView2 webView)
        {
            string script = @"
(() => {
    // Submit ボタンが表示されていなければ生成中（Stop ボタン表示中）
    const submitBtn = document.querySelector(
        'button[aria-label=""Submit""], button[type=""submit""]'
    );
    if (!submitBtn) return true;
    if (submitBtn.disabled) return true;
    // streaming クラスが body についていれば生成中
    if (document.body.classList.contains('streaming')) return true;
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
        const msgs = Array.from(document.querySelectorAll('.prose'));
        if (msgs.length > 0) return msgs[msgs.length - 1].innerText?.trim() ?? '';
        const fallback = Array.from(document.querySelectorAll('[data-testid=""answer""]'));
        if (fallback.length > 0) return fallback[fallback.length - 1].innerText?.trim() ?? '';
        return '';
    }

    function notifyDone(text) {
        window.__gironObserverActive = false;
        chrome.webview.postMessage(JSON.stringify({ type: 'GenerationDone', text: text, site: 'Perplexity' }));
    }

    const observer = new MutationObserver(() => {
        const text = getLatestText();
        if (text === window.__gironLastText) return;
        window.__gironLastText = text;
        if (window.__gironQuietTimer) clearTimeout(window.__gironQuietTimer);
        window.__gironQuietTimer = setTimeout(() => {
            const stopBtn = document.querySelector('button[aria-label=""Stop""]');
            if (!stopBtn) notifyDone(text);
        }, QUIET_MS);
    });

    observer.observe(document.body, { childList: true, subtree: true, characterData: true });
})();";
            await webView.ExecuteScriptAsync(script);
        }
    }
}
