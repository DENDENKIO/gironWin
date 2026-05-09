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
            return !string.IsNullOrWhiteSpace(url)
                   && url.Contains("gemini.google.com", StringComparison.OrdinalIgnoreCase);
        }

        public override async Task<bool> SetInputAsync(WebView2 webView, string text)
        {
            if (webView?.CoreWebView2 == null)
            {
                return false;
            }

            string escapedText = JsonSerializer.Serialize(text);

            string script = $@"
(() => {{
    const text = {escapedText};

    const selectors = [
        'div.ql-editor[contenteditable=""true""]',
        'div[role=""textbox""][contenteditable=""true""]',
        '[contenteditable=""true""]',
        '[role=""textbox""]',
        'textarea',
        'input[type=""text""]',
        'input:not([type])'
    ];

    function setNativeValue(element, value) {{
        const proto = Object.getPrototypeOf(element);
        const valueSetter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;
        if (valueSetter) {{
            valueSetter.call(element, value);
        }} else {{
            element.value = value;
        }}
    }}

    function placeCaretAtEnd(el) {{
        try {{
            const range = document.createRange();
            range.selectNodeContents(el);
            range.collapse(false);
            const sel = window.getSelection();
            sel.removeAllRanges();
            sel.addRange(range);
        }} catch (e) {{
        }}
    }}

    for (const selector of selectors) {{
        const elements = Array.from(document.querySelectorAll(selector));

        for (const el of elements) {{
            if (!el) continue;

            const style = window.getComputedStyle(el);
            if (style.display === 'none' || style.visibility === 'hidden') continue;

            el.focus();

            if (el.tagName === 'TEXTAREA' || el.tagName === 'INPUT') {{
                setNativeValue(el, text);
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
}})();
";

            string result = await webView.ExecuteScriptAsync(script);
            return result.Contains("true", StringComparison.OrdinalIgnoreCase);
        }

        public override async Task<bool> SendAsync(WebView2 webView)
        {
            if (webView?.CoreWebView2 == null)
            {
                return false;
            }

            string script = @"
(() => {
    const buttonSelectors = [
        'button[aria-label*=""Send""]',
        'button[aria-label*=""送信""]',
        'button[aria-label*=""プロンプトを送信""]',
        'button[data-test-id=""send-button""]',
        'button'
    ];

    for (const selector of buttonSelectors) {
        const buttons = Array.from(document.querySelectorAll(selector));

        for (const btn of buttons) {
            const text = (btn.innerText || btn.value || btn.getAttribute('aria-label') || '').toLowerCase();

            if (
                !btn.disabled &&
                (
                    text.includes('send') ||
                    text.includes('送信') ||
                    text.includes('submit') ||
                    selector !== 'button'
                )
            ) {
                btn.click();
                return true;
            }
        }
    }

    const inputSelectors = [
        'div.ql-editor[contenteditable=""true""]',
        'div[role=""textbox""][contenteditable=""true""]',
        '[contenteditable=""true""]',
        '[role=""textbox""]',
        'textarea'
    ];

    for (const selector of inputSelectors) {
        const el = document.querySelector(selector);
        if (!el) continue;

        el.focus();

        const down = new KeyboardEvent('keydown', {
            bubbles: true,
            cancelable: true,
            key: 'Enter',
            code: 'Enter'
        });

        const up = new KeyboardEvent('keyup', {
            bubbles: true,
            cancelable: true,
            key: 'Enter',
            code: 'Enter'
        });

        el.dispatchEvent(down);
        el.dispatchEvent(up);
        return true;
    }

    return false;
})();
";

            string result = await webView.ExecuteScriptAsync(script);
            return result.Contains("true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
