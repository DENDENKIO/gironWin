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
            return !string.IsNullOrWhiteSpace(url)
                   && url.Contains("perplexity.ai", StringComparison.OrdinalIgnoreCase);
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
    const el = document.querySelector('#ask-input[contenteditable=""true""][role=""textbox""]')
        || document.querySelector('#ask-input')
        || document.querySelector('[contenteditable=""true""]#ask-input');

    if (!el) {{
        return false;
    }}

    el.focus();

    try {{
        const range = document.createRange();
        range.selectNodeContents(el);
        range.collapse(false);
        const sel = window.getSelection();
        sel.removeAllRanges();
        sel.addRange(range);
    }} catch (e) {{
    }}

    try {{
        document.execCommand('selectAll', false, null);
        document.execCommand('delete', false, null);
    }} catch (e) {{
        el.textContent = '';
    }}

    let inserted = false;

    try {{
        inserted = document.execCommand('insertText', false, text);
    }} catch (e) {{
        inserted = false;
    }}

    if (!inserted) {{
        el.textContent = text;
    }}

    el.dispatchEvent(new InputEvent('input', {{
        bubbles: true,
        inputType: 'insertText',
        data: text
    }}));

    el.dispatchEvent(new Event('change', {{ bubbles: true }}));

    return true;
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
    const el = document.querySelector('#ask-input[contenteditable=""true""][role=""textbox""]')
        || document.querySelector('#ask-input')
        || document.querySelector('[contenteditable=""true""]#ask-input');

    if (!el) {
        return false;
    }

    el.focus();

    const keyDown = new KeyboardEvent('keydown', {
        key: 'Enter',
        code: 'Enter',
        which: 13,
        keyCode: 13,
        bubbles: true,
        cancelable: true
    });

    const keyPress = new KeyboardEvent('keypress', {
        key: 'Enter',
        code: 'Enter',
        which: 13,
        keyCode: 13,
        bubbles: true,
        cancelable: true
    });

    const keyUp = new KeyboardEvent('keyup', {
        key: 'Enter',
        code: 'Enter',
        which: 13,
        keyCode: 13,
        bubbles: true,
        cancelable: true
    });

    el.dispatchEvent(keyDown);
    el.dispatchEvent(keyPress);
    el.dispatchEvent(keyUp);

    return true;
})();
";

            string result = await webView.ExecuteScriptAsync(script);
            return result.Contains("true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
