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

            string script = $@"
(async () => {{
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

    // ★ contenteditable: クリア → rAF で1フレーム待つ → 確認 → paste
    // Step 1: 確実にクリア
    try {{
        // selection API でクリア
        const selection = window.getSelection();
        if (selection && el.childNodes.length > 0) {{
            const range = document.createRange();
            range.selectNodeContents(el);
            selection.removeAllRanges();
            selection.addRange(range);
            selection.deleteFromDocument();
        }}
        // innerHTML も空にする（二重保険）
        el.innerHTML = '';
        // React の input イベントを発火してstate同期
        el.dispatchEvent(new InputEvent('input', {{
            bubbles: true,
            inputType: 'deleteContentBackward'
        }}));
    }} catch(e) {{}}

    // Step 2: rAF で1フレーム待つ（React のバッチ更新完了を待つ）
    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));

    // Step 3: クリアされたか確認
    const afterClear = (el.innerText || el.textContent || '').trim();
    if (afterClear.length > 0) {{
        // まだ残っていたら強制クリア
        try {{
            el.innerHTML = '';
            await new Promise(resolve => requestAnimationFrame(resolve));
        }} catch(e) {{}}
    }}

    el.focus();

    if (!text) return 'clear-ok';

    // ① DataTransfer paste
    try {{
        const dt = new DataTransfer();
        dt.setData('text/plain', text);
        el.dispatchEvent(new ClipboardEvent('paste', {{
            bubbles: true, cancelable: true, clipboardData: dt
        }}));
        // paste後もrAF1フレーム待ってから長さ確認
        await new Promise(resolve => requestAnimationFrame(resolve));
        const cur = (el.innerText || el.textContent || '').trim();
        if (cur.length > 0) {{
            return 'paste-ok:' + cur.length;
        }}
    }} catch(e) {{}}

    // ② execCommand insertText
    try {{
        el.focus();
        const ok = document.execCommand('insertText', false, text);
        if (ok) {{
            return 'execCmd-ok';
        }}
    }} catch(e) {{}}

    // ③ 最終手段: textNode 直接挿入
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
            // ★ 変更箇所: TreeWalker → DFS手動走査
            // katex/katex-display ルートを発見したら outerHTML ごと取得して子孫に入らない
            // → katex-html(複雑span群) も katex-mathml(<math>) も outerHTML に含まれたまま保持
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
        const parts = [];

        function dfs(node) {
            if (!node) return;

            if (node.nodeType === 1) {
                const cls = node.getAttribute('class') || '';

                // ★ katex / katex-display ルート → outerHTML をそのまま挿入して子孫に入らない
                //   outerHTML には katex-mathml(<math>) と katex-html の両方が含まれる
                //   CSS注入により katex-html は非表示、katex-mathml(<math>) が表示される
                if (/\bkatex\b/.test(cls) || /\bkatex-display\b/.test(cls)) {
                    parts.push(node.outerHTML);
                    return;
                }

                // citation → 完全スキップ
                if (/\bcitation\b/.test(cls)) return;

                // 非表示 → スキップ
                if (!isVisible(node)) return;

                const tag = node.tagName.toLowerCase();
                const isBlock = ['p','h1','h2','h3','h4','h5','h6',
                                  'li','blockquote','pre','div','table','tr','td','th'].includes(tag);
                if (isBlock && parts.length > 0) {
                    const last = parts[parts.length - 1];
                    if (last !== '\n') parts.push('\n');
                }

                // 子を再帰処理
                for (const child of node.childNodes) {
                    dfs(child);
                }
            } else if (node.nodeType === 3) {
                const val = (node.nodeValue || '').trim();
                if (!val) return;
                // 1-2文字の数字のみ（引用番号）は除外
                if (val.length <= 2 && /^\d+$/.test(val)) return;
                parts.push(val);
            }
        }

        dfs(root);

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
    // ① Stop ボタンが存在する
    if (document.querySelector('button[aria-label=""Stop""], button[aria-label*=""Stop""]'))
        return true;

    // ② 送信ボタンが無効化されている（生成中は送信ボタンが disabled になる）
    const submitSelectors = [
        'button#ask-submit',
        'button[data-testid=""submit-button""]',
        'button[aria-label=""Submit""]',
        'button[aria-label*=""Send""]',
        'button[type=""submit""]'
    ];
    for (const sel of submitSelectors) {
        const btn = document.querySelector(sel);
        if (btn) {
            if (btn.disabled || btn.getAttribute('aria-disabled') === 'true')
                return true;
            return false;
        }
    }

    // ③ ローディングインジケーター
    if (document.querySelector('.animate-pulse, [data-generating=""true""], [aria-busy=""true""]'))
        return true;

    return false;
})();";
            return await ExecScriptBoolAsync(webView, script);
        }

        // ★ 変更箇所: 空 → CSS注入 + MutationObserver
        // katex-html を非表示、katex-mathml (<math>) を表示する CSS を <style> タグで注入
        // MutationObserver で動的レンダリング後も style が消えたら再注入する
        public override async Task InjectObserverAsync(WebView2 webView)
        {
            if (webView?.CoreWebView2 == null) return;

            string script = @"
(() => {
    const STYLE_ID = '__giron_katex_override';

    function injectStyle() {
        // 既に挿入済みならスキップ
        if (document.getElementById(STYLE_ID)) return;

        const style = document.createElement('style');
        style.id = STYLE_ID;
        style.textContent = [
            '.katex-html { display: none !important; }',

            '.katex-mathml {',
            '    position: static !important;',
            '    clip: auto !important;',
            '    clip-path: none !important;',
            '    width: auto !important;',
            '    height: auto !important;',
            '    overflow: visible !important;',
            '    visibility: visible !important;',
            '    white-space: normal !important;',
            '}',

            '.katex-mathml math {',
            '    display: inline-block !important;',
            '    font-size: 1em !important;',
            '}',

            /* ★ 追加: 親.katexをinline-blockにして高さを確保 */
            '.katex {',
            '    display: inline-block !important;',
            '}',

            '.katex-display {',
            '    display: block !important;',
            '    text-align: center !important;',
            '}',

            '.katex-display .katex-mathml math {',
            '    display: block !important;',
            '    text-align: center !important;',
            '    margin: 0.5em 0 !important;',
            '}'
        ].join('\n');

        (document.head || document.documentElement).appendChild(style);
    }

    // 初回注入
    injectStyle();

    // ページ遷移・動的レンダリングで <head> がリセットされても再注入する
    if (!window.__gironKatexObserver) {
        window.__gironKatexObserver = new MutationObserver(() => {
            injectStyle();
        });
        window.__gironKatexObserver.observe(document.documentElement, {
            childList: true,
            subtree: true
        });
    }
})();
";
            try
            {
                await webView.ExecuteScriptAsync(script);
                Log("[PerplexityObserver] katex CSS injected");
            }
            catch (Exception ex)
            {
                Log($"[PerplexityObserver] Error: {ex.Message}");
            }
        }
    }
}
