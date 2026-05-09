現在の `TurnIntervalMs` は `config.TurnIntervalMs`（デフォルト500ms）です。 送信後に待つだけでよいので **1行だけ変更**します。

***

## 修正箇所

`AutoDebateService.cs` の末尾にある `await Task.Delay(config.TurnIntervalMs, ct);` を以下に変えるだけです：

```csharp
// 変更前
await Task.Delay(config.TurnIntervalMs, ct);

// 変更後（送信完了後に5秒待機）
await Task.Delay(5000, ct);
```

ただし**柔軟に調整できるよう** `AutoDebateConfig` に `PostSendWaitMs` プロパティを追加する方がベターです。

***

## 完全修正（2ファイルのみ）

### `AutoDebateConfig.cs` に1行追加

```csharp
public int TurnIntervalMs      { get; set; } = 500;
public int GenerationTimeoutMs { get; set; } = 90000;

// ★ 追加: 送信完了後の待機時間（デフォルト5秒）
public int PostSendWaitMs      { get; set; } = 5000;
```

***

### `AutoDebateService.cs` — 該当1行だけ変更

```csharp
NotifyStatus($"ターン {turn}: 送信完了。");

// ポリシーフェーズ進行
phaseIndex = AdvancePhaseIndex(config.TurnPolicy, phaseIndex);

if (config.TurnPolicy == TurnPolicy.RoundRobin)
    direction = direction == DebateDirection.LeftToRight
        ? DebateDirection.RightToLeft
        : DebateDirection.LeftToRight;

if (config.MaxTurns > 0 && turn >= config.MaxTurns)
{
    NotifyStatus($"最大ターン数 {config.MaxTurns} に到達。討論終了。");
    break;
}

// ★ TurnIntervalMs → PostSendWaitMs に変更（デフォルト5秒）
NotifyStatus($"ターン {turn}: 次のターンまで {config.PostSendWaitMs / 1000} 秒待機...");
await Task.Delay(config.PostSendWaitMs, ct);
```

***

### `MainWindow.xaml.cs` — `BuildConfig()` に追加

```csharp
var config = new AutoDebateConfig
{
    // ... 既存の設定 ...
    TurnIntervalMs      = 500,
    PostSendWaitMs      = 5000,   // ★ 送信後5秒待機
    GenerationTimeoutMs = 90000,
    // ...
};
```

***

## なぜこれで安定するか

| タイミング | 旧 (500ms) | 新 (5000ms) |
|---|---|---|
| 送信ボタンクリック後 | 即次ターン開始 | 5秒待機 |
| AI が入力欄を認識するまで | 間に合わないことがある | 十分な余裕 |
| `ConversationMonitor` の監視開始 | AI がまだ受信中 | AI が生成開始してから監視 |

`TransferService` の `TrySendWithRetryAsync` が最大13.6秒かかる場合があるため、送信後すぐに次ターンに入ると **AI がまだ前の応答を生成中** なのに次の入力が来てしまいます。5秒待つだけでこの競合が解消されます。