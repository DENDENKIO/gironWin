了解です。では次は、**左で選択した文を取得して、右の入力欄へ自動で入れて送信する**ところを作ります。WebView2 では `ExecuteScriptAsync()` でページ側 JavaScript を実行でき、入力欄への値設定やクリック操作の土台を作れます。 [chishiki21.blogspot](https://chishiki21.blogspot.com/2021/10/webview2-executescriptasync.html)

## 今回の方針

最初は「サイト依存をなるべく減らす」ため、右側の入力先を **複数候補のセレクタ** で探し、`textarea`、`input[type="text"]`、`contenteditable="true"` の順で試す方式にします。`input` イベントは入力欄や contenteditable に対して重要なので、値を入れたあとに `input` を発火させます。 [boxofcuriosities.co](https://boxofcuriosities.co.uk/post/how-to-dispatched-an-input-event-for-a-contenteditable-and)

また、送信は最初から完全自動にせず、**右へ入力だけ** と **右へ入力して送信** を分けます。  
これで、右サイトごとの癖を見ながら安全に確認できます。 [web.biz-prog](https://web.biz-prog.net/praxis/webview/keyinput.html)

## `MainWindow.xaml` に追加するもの

まず、上の操作バーに次の 3 つを追加してください。

- 左選択 → 右入力
- 左選択 → 右送信
- 橋渡し文を付けるチェックボックス

`DockPanel` の最後あたりに、これを追加します。

```xml
<CheckBox x:Name="AppendBridgeCheckBox"
          Margin="16,0,8,0"
          VerticalAlignment="Center"
          IsChecked="True"
          Content="橋渡し文を付ける" />

<Button x:Name="SendLeftSelectionToRightInputButton"
        Width="150"
        Margin="0,0,8,0"
        Content="左選択→右入力"
        Click="SendLeftSelectionToRightInputButton_Click"/>

<Button x:Name="SendLeftSelectionToRightSubmitButton"
        Width="150"
        Content="左選択→右送信"
        Click="SendLeftSelectionToRightSubmitButton_Click"/>
```

## 追加する C# コード

`MainWindow.xaml.cs` に、まず次のメソッドを追加してください。  
これは、左の選択文を取って、必要なら橋渡し文を付ける処理です。 [learn.microsoft](https://learn.microsoft.com/ja-jp/microsoft-edge/webview2/how-to/javascript)

```csharp
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
```

次に、右入力欄へテキストを入れるメソッドです。  
`textarea`、通常 input、contenteditable を順に試します。 [web.biz-prog](https://web.biz-prog.net/praxis/webview/jquery.html)

```csharp
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
```

次に、右側の送信ボタンを押す処理です。  
ボタン候補をテキストや属性で探します。 [stackoverflow](https://stackoverflow.com/questions/68623411/webview2-executescriptasync-to-click-an-input-button)

```csharp
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
```

## ボタンイベントを追加

最後に、ボタンクリック処理を追加します。

```csharp
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
```

## まずの確認方法

次の順で試してください。

1. 左側で適当な文章を選択する。  
2. 「左選択→右入力」を押す。  
3. 右側の入力欄に文字が入るか確認する。  
4. 問題なければ「左選択→右送信」を試す。  

この段階では、**送信ボタン検出はサイト依存で外れることがある**ので、まずは入力成功を目標にするのがよいです。 [web.biz-prog](https://web.biz-prog.net/praxis/webview/jquery.html)

## 注意点

AI チャットサイトは `textarea` ではなく `contenteditable` や独自 UI を使うことがあるため、汎用処理だけでは 100% は通りません。 [developer.mozilla](https://developer.mozilla.org/en-US/docs/Web/API/Element/input_event)
そのため、次の段階では右サイトごとに「入力欄セレクタ」と「送信ボタンセレクタ」を持つ **サイト別アダプタ** に進めるのが自然です。 [github](https://github.com/microsoft/microsoft-ui-xaml-specs/blob/master/active/WebView2/WebView2_spec.md)

## 次の段階

この半自動フローが通ったら、次は次のどちらかです。

- 右だけでなく **右選択 → 左入力** の逆方向も作る。  
- 右サイト専用の入力・送信ロジックを作って成功率を上げる。  

