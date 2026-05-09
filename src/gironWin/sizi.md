よかったです。そこまで通ればかなり前進です。  
右サイトは Gemini の URL に変更できますし、ボタンが隠れる件は、上部バーを `DockPanel` のまま横一列にしているのが原因なので、**折り返し可能な `WrapPanel` か `ScrollViewer` 付きに変える**のが簡単です。 [learn.microsoft](https://learn.microsoft.com/en-us/answers/questions/858402/wpf-wrappanel-horizontal-and-vertical-scrolling)

## Gemini の URL

右側の初期 URL はこれに変更してください。 [learn.microsoft](https://learn.microsoft.com/en-us/microsoft-edge/webview2/get-started/wpf)

```xml
Text="https://gemini.google.com/app?hl=ja"
```

つまり `RightUrlTextBox` を次のようにします。

```xml
<TextBox x:Name="RightUrlTextBox"
         Width="320"
         Margin="0,0,8,0"
         VerticalContentAlignment="Center"
         Text="https://gemini.google.com/app?hl=ja" />
```

## ボタンが隠れる原因

今は上部が `DockPanel` で横一列なので、ウィンドウ幅が足りないと右端のボタンが見切れやすいです。  
WPF では、並べたコントロールを折り返して見せたいときは `WrapPanel` が向いています。 [wpf-tutorial](https://wpf-tutorial.com/hu/25/panels/the-wrappanel-control/)

## 一番簡単な直し方

上部バーを `ScrollViewer + WrapPanel` に変えてください。  
これならボタンが増えても、折り返しまたはスクロールで見えるようになります。 [learn.microsoft](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/how-to-use-the-content-scrolling-methods-of-scrollviewer)

### 置き換える `MainWindow.xaml` の上部部分

今の `DockPanel Grid.Row="0"` を、これに置き換えてください。

```xml
<ScrollViewer Grid.Row="0"
              HorizontalScrollBarVisibility="Auto"
              VerticalScrollBarVisibility="Disabled"
              Margin="8">
    <WrapPanel Orientation="Horizontal" VerticalAlignment="Center">
        <TextBox x:Name="LeftUrlTextBox"
                 Width="280"
                 Margin="0,0,8,8"
                 VerticalContentAlignment="Center"
                 Text="https://www.perplexity.ai/" />

        <Button x:Name="LeftGoButton"
                Width="80"
                Margin="0,0,8,8"
                Content="左へ移動"
                Click="LeftGoButton_Click"/>

        <Button x:Name="LeftTitleButton"
                Width="110"
                Margin="0,0,8,8"
                Content="左タイトル取得"
                Click="LeftTitleButton_Click"/>

        <Button x:Name="LeftSelectionButton"
                Width="110"
                Margin="0,0,8,8"
                Content="左選択取得"
                Click="LeftSelectionButton_Click"/>

        <TextBox x:Name="RightUrlTextBox"
                 Width="280"
                 Margin="0,0,8,8"
                 VerticalContentAlignment="Center"
                 Text="https://gemini.google.com/app?hl=ja" />

        <Button x:Name="RightGoButton"
                Width="80"
                Margin="0,0,8,8"
                Content="右へ移動"
                Click="RightGoButton_Click"/>

        <Button x:Name="RightTitleButton"
                Width="110"
                Margin="0,0,8,8"
                Content="右タイトル取得"
                Click="RightTitleButton_Click"/>

        <Button x:Name="RightSelectionButton"
                Width="110"
                Margin="0,0,8,8"
                Content="右選択取得"
                Click="RightSelectionButton_Click"/>

        <CheckBox x:Name="AppendBridgeCheckBox"
                  Margin="8,0,8,8"
                  VerticalAlignment="Center"
                  IsChecked="True"
                  Content="橋渡し文を付ける" />

        <Button x:Name="SendLeftSelectionToRightInputButton"
                Width="150"
                Margin="0,0,8,8"
                Content="左選択→右入力"
                Click="SendLeftSelectionToRightInputButton_Click"/>

        <Button x:Name="SendLeftSelectionToRightSubmitButton"
                Width="150"
                Margin="0,0,8,8"
                Content="左選択→右送信"
                Click="SendLeftSelectionToRightSubmitButton_Click"/>
    </WrapPanel>
</ScrollViewer>
```

これで、幅が足りないときに右端が消えにくくなります。 [learn.microsoft](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/how-to-use-the-content-scrolling-methods-of-scrollviewer)

## Gemini について

Gemini は通常の `textarea` ではなく、`contenteditable` 系やフレームワーク依存の入力処理を使うことがあるので、**値を入れただけでは送信ボタンが有効化されないことがあります**。React 系やリッチ入力系では、ネイティブ setter と `input` イベントに加えて、キーイベントが必要になる場合があります。 [reddit](https://www.reddit.com/r/PromptEngineering/comments/1rd04q2/i_got_tired_of_rewriting_the_same_prompts_every/)

つまり、今の汎用ロジックで入力まで成功しても、Gemini 向けには次の微調整が必要になる可能性があります。

- `contenteditable="true"` 優先
- `input` に加えて `change`
- 送信前に `keydown` / `keyup`
- 送信ボタン候補を Gemini 向けに調整 [reddit](https://www.reddit.com/r/PromptEngineering/comments/1rd04q2/i_got_tired_of_rewriting_the_same_prompts_every/)

## 今やるべきこと

まずは次の 2 点をやってください。

1. 上部バーを `ScrollViewer + WrapPanel` に置き換える。 [learn.microsoft](https://learn.microsoft.com/en-us/answers/questions/858402/wpf-wrappanel-horizontal-and-vertical-scrolling)
2. 右 URL を Gemini に変える。  

そのあと、  
- 「左選択→右入力」が Gemini で通るか  
- 「左選択→右送信」で送信まで行くか  
を確認してください。

## 次の段階

Gemini で入力や送信が少し不安定なら、次は **Gemini 専用の入力欄セレクタと送信処理** を作るのが最善です。  
特に Gemini は汎用セレクタより専用対応の方が成功率が上がりやすいです。 [reddit](https://www.reddit.com/r/PromptEngineering/comments/1rd04q2/i_got_tired_of_rewriting_the_same_prompts_every/)

