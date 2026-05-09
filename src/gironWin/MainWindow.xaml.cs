using Microsoft.Web.WebView2.Core;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace gironWin
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializeWebViewsAsync();
        }

        private async Task InitializeWebViewsAsync()
        {
            var env = await CoreWebView2Environment.CreateAsync();

            await LeftWebView.EnsureCoreWebView2Async(env);
            await RightWebView.EnsureCoreWebView2Async(env);

            LeftWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            LeftWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;

            RightWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            RightWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;

            LeftWebView.Source = new Uri(LeftUrlTextBox.Text);
            RightWebView.Source = new Uri(RightUrlTextBox.Text);
        }

        private void LeftGoButton_Click(object sender, RoutedEventArgs e)
        {
            if (Uri.TryCreate(LeftUrlTextBox.Text, UriKind.Absolute, out var uri))
            {
                LeftWebView.Source = uri;
            }
            else
            {
                MessageBox.Show("左URLが不正です。");
            }
        }

        private void RightGoButton_Click(object sender, RoutedEventArgs e)
        {
            if (Uri.TryCreate(RightUrlTextBox.Text, UriKind.Absolute, out var uri))
            {
                RightWebView.Source = uri;
            }
            else
            {
                MessageBox.Show("右URLが不正です。");
            }
        }

        private async Task<string> ExecuteScriptStringAsync(Microsoft.Web.WebView2.Wpf.WebView2 webView, string script)
        {
            if (webView?.CoreWebView2 == null)
            {
                return string.Empty;
            }

            string json = await webView.ExecuteScriptAsync(script);

            if (string.IsNullOrWhiteSpace(json) || json == "null")
            {
                return string.Empty;
            }

            try
            {
                return JsonSerializer.Deserialize<string>(json) ?? string.Empty;
            }
            catch
            {
                return json.Trim('"');
            }
        }

        private async void LeftTitleButton_Click(object sender, RoutedEventArgs e)
        {
            string title = await ExecuteScriptStringAsync(LeftWebView, "document.title");
            MessageBox.Show(string.IsNullOrWhiteSpace(title) ? "タイトルを取得できませんでした。" : title, "左タイトル");
        }

        private async void RightTitleButton_Click(object sender, RoutedEventArgs e)
        {
            string title = await ExecuteScriptStringAsync(RightWebView, "document.title");
            MessageBox.Show(string.IsNullOrWhiteSpace(title) ? "タイトルを取得できませんでした。" : title, "右タイトル");
        }

        private async void LeftSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            string selectedText = await ExecuteScriptStringAsync(
                LeftWebView,
                "window.getSelection ? window.getSelection().toString() : ''");

            MessageBox.Show(string.IsNullOrWhiteSpace(selectedText) ? "左側で選択された文字がありません。" : selectedText, "左選択テキスト");
        }

        private async void RightSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            string selectedText = await ExecuteScriptStringAsync(
                RightWebView,
                "window.getSelection ? window.getSelection().toString() : ''");

            MessageBox.Show(string.IsNullOrWhiteSpace(selectedText) ? "右側で選択された文字がありません。" : selectedText, "右選択テキスト");
        }

        private async Task<string> GetLeftSelectedTextForTransferAsync()
        {
            string selectedText = await ExecuteScriptStringAsync(
                LeftWebView,
                "window.getSelection ? window.getSelection().toString() : ''");

            if (string.IsNullOrWhiteSpace(selectedText))
            {
                return string.Empty;
            }

            if (AppendBridgeCheckBox.IsChecked == true)
            {
                return $"{selectedText}\n\nこのように考えていますがどうですか？";
            }

            return selectedText;
        }

        private async Task<bool> SetTextToRightInputAsync(string text)
        {
            if (RightWebView?.CoreWebView2 == null)
            {
                return false;
            }

            string escapedText = JsonSerializer.Serialize(text);

            string script = $@"
(() => {{
    const text = {escapedText};

    const selectors = [
        'textarea',
        'input[type=""text""]',
        'input:not([type])',
        '[contenteditable=""true""]',
        '[role=""textbox""]'
    ];

    function setNativeValue(element, value) {{
        const valueSetter = Object.getOwnPropertyDescriptor(element.__proto__, 'value')?.set;
        if (valueSetter) {{
            valueSetter.call(element, value);
        }} else {{
            element.value = value;
        }}
    }}

    for (const selector of selectors) {{
        const el = document.querySelector(selector);
        if (!el) continue;

        el.focus();

        if (el.tagName === 'TEXTAREA' || el.tagName === 'INPUT') {{
            setNativeValue(el, text);
            el.dispatchEvent(new Event('input', {{ bubbles: true }}));
            el.dispatchEvent(new Event('change', {{ bubbles: true }}));
            return true;
        }}

        if (el.isContentEditable || el.getAttribute('contenteditable') === 'true' || el.getAttribute('role') === 'textbox') {{
            el.focus();
            el.textContent = text;
            el.dispatchEvent(new Event('input', {{ bubbles: true }}));
            return true;
        }}
    }}

    return false;
}})();
";

            string result = await RightWebView.ExecuteScriptAsync(script);
            return result.Contains("true", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<bool> ClickRightSendAsync()
        {
            if (RightWebView?.CoreWebView2 == null)
            {
                return false;
            }

            string script = @"
(() => {
    const candidates = Array.from(document.querySelectorAll('button, input[type=""submit""], [role=""button""]'));

    const keywords = ['send', '送信', 'submit'];

    for (const el of candidates) {
        const text = (el.innerText || el.value || el.getAttribute('aria-label') || '').toLowerCase();

        if (keywords.some(k => text.includes(k))) {
            el.click();
            return true;
        }
    }

    return false;
})();
";

            string result = await RightWebView.ExecuteScriptAsync(script);
            return result.Contains("true", StringComparison.OrdinalIgnoreCase);
        }

        private async void SendLeftSelectionToRightInputButton_Click(object sender, RoutedEventArgs e)
        {
            string text = await GetLeftSelectedTextForTransferAsync();

            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("左側で選択された文字列がありません。");
                return;
            }

            bool ok = await SetTextToRightInputAsync(text);

            MessageBox.Show(ok ? "右側の入力欄にテキストを入れました。" : "右側の入力欄が見つかりませんでした。");
        }

        private async void SendLeftSelectionToRightSubmitButton_Click(object sender, RoutedEventArgs e)
        {
            string text = await GetLeftSelectedTextForTransferAsync();

            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("左側で選択された文字列がありません。");
                return;
            }

            bool inputOk = await SetTextToRightInputAsync(text);
            if (!inputOk)
            {
                MessageBox.Show("右側の入力欄が見つかりませんでした。");
                return;
            }

            bool sendOk = await ClickRightSendAsync();

            MessageBox.Show(sendOk ? "右側へ入力して送信しました。" : "入力はできましたが送信ボタンが見つかりませんでした。");
        }
    }
}