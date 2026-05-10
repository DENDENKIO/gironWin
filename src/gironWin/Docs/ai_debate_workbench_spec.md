# AI討論ワークベンチ 要件定義書・詳細設計・開発ロードマップ

> **最終更新: 2026-05-10**  
> 実装リポジトリ: [DENDENKIO/gironWin](https://github.com/DENDENKIO/gironWin)

---

## 概要
本書は、Windows 上で複数の AI チャットサイトを専用ブラウザのように表示し、生成文章を自動取得・相互受け渡し・議論進行・人間介入・引用管理できる「AI討論ワークベンチ」の要件定義書、詳細設計方針、および開発ロードマップをまとめたものである。WebView2 は Windows デスクトップアプリへ Edge ベースの Web コンテンツを埋め込め、JavaScript 実行やページとのメッセージ連携が可能であるため、本システムの基盤として採用している。

本システムは単なる自動往復ツールではなく、human-in-the-loop の承認ゲート、途中介入、引用返信、役割ベース討論、研究用途向けの厳密性チェックを備えた、汎用的な議論オーケストレーション基盤を目指す。

---

## プロダクト定義

### 目的
ユーザーが指定した議題、立場、役割、研究テーマ、設計テーマに対して、複数の AI チャットサイトおよび人間参加者を組み合わせ、建設的・批判的・査読的・数学的・創造的な議論を継続的に行えるデスクトップアプリを提供する。

### プロダクトの核
本プロダクトの核は次の 6 点である。

- 複数 AI サイトを同時表示し、発言を自動取得するブラウザ操作基盤
- 発言を相手へ自動または承認付きで送る討論オーケストレーター
- ユーザーが司会・討論者・査読者として参加できる 3 席以上の人間参加型設計
- 過去ログからの全文・部分引用と、引用元追跡機能
- 数学研究や設計レビューに対応する厳密性・反例・未解決点の構造化
- 承認制、停止条件、ログ記録、エクスポートを含む運用安全性

---

## 想定ユースケース

### 主要ユースケース
| ユースケース | 内容 | 必要機能 |
|---|---|---|
| 建設的討論 | AI 同士が案を出し、批判し、改善する | 役割設定、受け渡しテンプレート、司会要約 |
| ユーザー司会 | ユーザーが途中で論点整理し次の問いを投げる | 途中介入、手動司会ターン、承認待ち |
| ユーザー討論参加 | ユーザーが片側として発言し、AI が対戦する | 人間席、入力欄、送信制御 |
| アプリ設計レビュー | 要件、仕様、UI、リスクを AI と議論する | 引用、承認、成果物化 |
| 数学研究 | 命題、証明案、反例候補、未解決点を整理する | 厳密性タグ、反例探索、構造化ログ |
| 査読モード | 文書・論文・設計に対する批判と改善 | 査読役、改善役、要約役 |

### ペルソナ
- 高度な開発者、研究者、企画担当者、仕様策定者、教育用途の利用者を主対象とする。
- AI を単発利用するのではなく、複数視点を競合させながら品質を上げたいユーザーを想定する。

---

## システムスコープ

### 対象機能
- 左右 2 つ以上の AI チャットサイト表示
- AI 発言の自動生成完了検知
- 生成文章の自動取得
- 相手側への自動入力と送信
- 人間席の参加
- 承認制送信
- 途中介入
- 引用返信
- ログ保存
- 役割テンプレート
- 研究・数学向けモード
- 結果エクスポート

### 対象外
- 各 AI サイトの公式 API 提供を前提とした実装
- クラウド常時同期
- 自動課金・契約管理
- 外部サイトの利用規約を無視した過度な自動化

---

## 機能要件

### FR-01 マルチペイン表示 ✅ 実装済み
システムは最低 3 席の参加者概念を持ち、左右 2 つの WebView2 と、第 3 席の Human / AI 切替パネルを提供する。

**実装クラス:** `MainWindow.xaml` / `MainWindow.xaml.cs`  
**実装詳細:**
- 左ペイン (`LeftWebView`)・右ペイン (`RightWebView`) の 2 WebView2
- 上部ツールバー: 開始・停止・一時停止/再開・介入ボタン
- 右サイドバー: `TurnLogListBox` によるターン履歴
- ステータスバー: `StatusTextBlock` にタイムスタンプ付きメッセージ
- `LeftSiteLabel` / `RightSiteLabel`: ナビゲーション完了時にサイト名を自動表示
- URL 入力欄 (`LeftUrlTextBox`, `RightUrlTextBox`) から任意のサイトへ遷移可能

---

### FR-02 サイト別アダプタ ✅ 実装済み
システムは AI サイトごとに入力欄、送信ボタン、メッセージ抽出位置、生成中判定、エラー処理を持つアダプタを提供する。

**実装クラス:**
- `IAiSiteAdapter.cs` — インターフェース定義
- `BaseAiSiteAdapter.cs` — 共通ヘルパー (`ExecScriptBoolAsync`, `ExecScriptStringAsync`)
- `GeminiAdapter.cs` — Google Gemini 対応アダプタ ✅
- `PerplexityAdapter.cs` — Perplexity AI 対応アダプタ ✅
- `AiSiteAdapterResolver.cs` — URL から適切なアダプタを選択するリゾルバ

**アダプタ共通インターフェース:**
```
IAiSiteAdapter
- string SiteName
- bool CanHandle(string url)
- Task<bool> SetInputAsync(WebView2 webView, string text)
- Task<bool> SendAsync(WebView2 webView)
- Task<string> ExtractLatestAsync(WebView2 webView)
- Task<bool> IsGeneratingAsync(WebView2 webView)
- Task InjectObserverAsync(WebView2 webView)
```

**Perplexity アダプタ実装詳細:**
- 入力欄セレクタ: `#ask-input[contenteditable="true"]` 他 6 種フォールバック
- テキスト取得: `div[id^="markdown-content-"]` を TreeWalker で走査（重複なし・順序保証）
- citation ノイズ除去: 2 文字以下の数字ノードを除外
- フォールバック: `.prose` → `[data-renderer="lm"]` の順
- 生成中判定: `Stop` ボタン / `.animate-pulse` / `[aria-busy="true"]`

**Gemini アダプタ実装詳細:**
- 入力欄セレクタ: `rich-textarea div[contenteditable]` 他
- テキスト取得: `.model-response-text` 他複数フォールバック
- 生成中判定: `mat-progress-bar` / `[data-is-loading]` / `stop-button`

---

### FR-03 自動生成完了検知 ✅ 実装済み
システムはポーリング＋アダプタ判定により DOM の変化を監視し、一定時間テキストが増加しない、生成中 UI が消えるなどの条件を満たしたとき生成完了と判定する。

**実装クラス:** `ConversationMonitor.cs`

**実装詳細:**
- `WaitForCompletionAsync(snapshot, timeoutMs, ct)` がメインエントリ
- ポーリング間隔: 500ms
- quiet period: テキスト変化なし + `IsGeneratingAsync == false` の状態が 2 回連続で確認されたら完了
- タイムアウト: `GenerationTimeoutMs`（デフォルト 90,000ms）
- スナップショット比較でテキスト未変化の場合はリトライ
- 連続失敗 3 回で討論停止 (`MaxConsecutiveFail`)

---

### FR-04 自動取得と保存 ✅ 実装済み
システムは最新発言、1 行要約、messageId を自動保存する。

**実装クラス:** `TransferRecord.cs`, `SessionRepository.cs`, `LogRepository.cs`

**TransferRecord フィールド:**
```csharp
int    TurnNumber
string Direction       // "Gemini→Perplexity" 形式
string Text            // 生テキスト全文
string Summary         // 1行要約
string MessageId       // "msg-{turn}-L/R" 形式
List<string> QuotedMessageIds
```

**SessionRepository 実装詳細:**
- `AppendAsync(TransferRecord)` でメモリ保持
- `AppendResearchTagAsync(ResearchTagEntry)` で研究タグ保持
- `ToTransferRecords()` でエクスポート用変換
- `ResearchTags` プロパティで全タグ参照
- JSON ファイルへの永続化 (`%AppData%/gironWin/sessions/`)

---

### FR-05 相互受け渡し ✅ 実装済み
システムは片側の発言を整形し、橋渡し文を付加して相手側の入力欄へ自動入力できる。

**実装クラス:** `TransferService.cs`

**実装詳細:**
- `TransferAsync(src, tgt, srcUrl, tgtUrl, submit, appendBridge, manualText)` がメイン
- `AppendBridge=true` 時: `"この意見についてどう考えますか？"` を末尾付加
- `manualText` が指定された場合は手動テキストをそのまま送信（プリセット/承認後テキストに対応）
- 送信失敗時は `TransferResult.Success=false` を返しリトライ制御に委譲
- `PostSendWaitMs`（デフォルト 5,000ms）の待機後に次ターンへ移行

**送信フォーマット:**
```
{tgtSystemPrompt}

[Turn {n} {src}→{tgt}{roleLabel}]
{generatedText}

（AppendBridge時）この意見についてどう考えますか？
```

---

### FR-06 役割・人格・視点設定 ✅ 実装済み
各席は参加者タイプと議論ロールを分離して設定できる。

**実装クラス:** `PromptProfile.cs`, `RoleSettingsWindow.xaml/.cs`, `DebateModels.cs`

**PromptProfile フィールド:**
```csharp
string LeftName            // 左席の表示名
string RightName           // 右席の表示名
string LeftSystemPrompt    // 左席のシステムプロンプト
string RightSystemPrompt   // 右席のシステムプロンプト
string Topic               // 議題
```

**DebateRole 列挙:**
`Moderator`, `Debater`, `Critic`, `Refiner`, `Reviewer`, `Researcher`

**プリセットとの優先順位:**
- プリセット設定 (`DebatePreset.LeftPrompt`) が非空の場合はプリセット優先
- 空の場合は `PromptProfile.LeftSystemPrompt` にフォールバック
- 議題は `TopicTextBox.Text` が最優先、空なら `PromptProfile.Topic`

---

### FR-07 人間参加 ✅ 実装済み（第3席 Human モード）
各席には Human または AI を割り当てできる。

**実装クラス:** `ThirdSeatWindow.xaml/.cs`, `DebateModels.cs`

**ThirdSeatConfig フィールド:**
```csharp
ThirdSeatMode Mode          // Disabled / Human / AiSite
DebateRole    Role          // 役割
string        DisplayName   // 表示名
int           IntervalTurns // 何ターンおきに発言するか
string        Url           // AiSite モード時の URL
WebView2?     WebView       // AiSite モード時のビュー（将来拡張）
```

**ThirdSeatMode:**
- `Disabled` — 第3席なし
- `Human` — 指定ターン間隔でポップアップ表示、ユーザーが手動入力
- `AiSite` — URL 設定可（WebView は将来実装予定）

**ThirdSeatWindow 動作:**
- モーダルレスで表示（`win.Show()`）
- ユーザーが送信するとコールバックで討論ループへ注入

---

### FR-08 途中介入 ✅ 実装済み
システムは実行中に IntervationWindow を開き、左・右・両方の席へテキストを注入できる。

**実装クラス:** `InterventionWindow.xaml/.cs`

**実装詳細:**
- 介入中は `_debateService.Pause()` で討論を一時停止
- 送信先: `Left` / `Right` / `Both` の 3 択
- 送信後または介入キャンセル後に `_debateService.Resume()`
- `InjectInterventionAsync` で各席のアダプタを通じて入力・送信

---

### FR-09 承認制送信 ✅ 実装済み
送信は条件ルールベースで承認制をオンオフ可能。

**実装クラス:** `ApprovalPolicy.cs`, `ApprovalQueue.cs`, `ApprovalWindow.xaml/.cs`

**ApprovalPolicy ルール:**
```csharp
bool RequireApprovalAlways          // 常時承認
bool RequireApprovalWhenQuoted      // 引用あり時
bool RequireApprovalForLongMessage  // 長文（デフォルト 1800文字）
bool RequireApprovalForCodeOrSpec   // コード・仕様含有時
bool RequireApprovalAfterRecovery   // エラー復帰後
int  LongMessageThreshold
```

**ApprovalPolicy.Default:** 全条件 OFF（FullAuto）  
**ApprovalPolicy.FullAuto:** 全条件 OFF（`RequireApprovalCheckBox` 非チェック時）

**ApprovalQueue 動作:**
- `EnqueueAsync(src, tgt, text, allowEdit, ct)` でキュー投入
- `ApprovalRequested` イベントで UI に通知（`ApprovalPanel` を表示）
- UI 側で `Approve(editedText)` または `Reject()` を呼ぶ
- 承認待ち中は討論ループが `await` でブロック

---

### FR-10 引用返信 ✅ 実装済み（基本実装）
ユーザーは過去の発言をクリックして内容を確認し、引用 ID を保持できる。

**実装クラス:** `QuoteService.cs`, `TextPreviewWindow.xaml/.cs`

**QuoteReference モデル:**
```csharp
string QuoteId
string SourceMessageId
int    SourceTurnNumber
string QuotedText
string SourceDirection
QuoteType Type     // Full / Partial
DateTime CreatedAt
```

**TextPreviewWindow 機能:**
- ターンログをクリックで発言プレビューウィンドウを開く
- 発言の全文・送信先・ターン番号を表示
- `QuoteService` に `AddFullQuote` / `AddPartialQuote` で登録
- `SessionRepository` に `AppendQuoteAsync` で保存

---

### FR-11 ログ追跡 ✅ 実装済み
各発言には TurnNumber、Direction、Text、Summary、MessageId、QuotedMessageIds を保持する。

**実装クラス:** `TransferRecord.cs`, `SessionRepository.cs`

---

### FR-12 司会支援 ⬜ 未実装（Phase 4 予定）
自動での論点整理・合意点・対立点サマリーは未実装。`SummaryService.cs` による 1 行要約は実装済み。

**実装クラス:** `SummaryService.cs` ✅（1行要約のみ）

**SummaryService 実装詳細:**
- 先頭 60 文字切り出し + 末尾 `...` 付加（シンプル実装）
- 将来: LLM API による構造化要約に置き換え予定

---

### FR-13 数学・研究モード ✅ 実装済み（基本実装）
研究タグの自動抽出と一覧表示が実装済み。

**実装クラス:** `ResearchService.cs`, `ResearchNoteWindow.xaml/.cs`

**ResearchService 実装詳細:**
- `ExtractAndAdd(text, turn, msgId)` でテキストからタグを抽出
- キーワードパターンマッチングで `ResearchTagEntry` を生成
- `Entries` コレクションで全タグ保持
- `ResearchTagsExtracted` イベントで UI に通知

**ResearchTagEntry フィールド:**
```csharp
string TagType     // 研究タグ種別
string Text        // タグが付いたテキスト
int    TurnNumber
string MessageId
```

**ResearchNoteWindow 機能:**
- 抽出済みタグをリスト形式で表示
- タグ種別・ターン番号でフィルタリング

**未実装タグ種別:** `Proposition`, `Definition`, `Assumption`, `ProofIdea`, `LemmaCandidate`, `Counterexample`, `Gap`, `Unverified`, `Derived`（将来の厳密実装で導入予定）

---

### FR-14 成果物生成 ✅ 実装済み
議論終了後に Markdown / JSON / TXT でエクスポートできる。

**実装クラス:** `ExportService.cs`

**エクスポート形式:**
- `ExportMarkdownAsync` — ターンログ・引用・研究タグ・プリセット情報を Markdown 出力
- `ExportJsonAsync` — 全データを構造化 JSON 出力
- `ExportTxtAsync` — プレーンテキスト出力
- 出力先: `SaveFileDialog` でユーザー指定

---

## 新規実装仕様（仕様書初出）

### NS-01 往復カウント方式 ✅ 実装済み
`MaxTurns` は「左右の送受信の総回数」ではなく「往復数（右が返答したら1往復）」としてカウントする。

**実装詳細 (`AutoDebateService.cs`):**
```csharp
int roundCount = 0;
// 右→左（右席の返答）完了時に +1
if (!isLeftTurn)
{
    roundCount++;
    if (config.MaxTurns > 0 && roundCount >= config.MaxTurns) break;
}
```

**設定値の意味:**

| MaxTurns | 動作 |
|---|---|
| 0 | 無制限 |
| 1 | 左1回・右1回（1往復）で終了 |
| 10 | 左10回・右10回（10往復）で終了 |

---

### NS-02 DebatePreset システム ✅ 実装済み
ビルトインの討論プリセットを選択・適用できる。

**実装クラス:** `DebateModels.cs`, `PresetSelectorWindow.xaml/.cs`

**DebatePreset フィールド:**
```csharp
string     Name
string     Description
TurnPolicy TurnPolicy
string     LeftPrompt
string     RightPrompt
string     Topic
bool       ResearchMode
```

**ビルトインプリセット (`PresetSelectorWindow.xaml.cs`):**
| プリセット名 | TurnPolicy | ResearchMode |
|---|---|---|
| 建設的討論 | CritiqueThenRefine | false |
| アプリ設計レビュー | RoundRobin | false |
| 数学研究 | ResearchReviewLoop | true |
| ソクラテス式問答 | ModeratorSelect | false |
| なりきり議論 | RoundRobin | false |

**UI 反映:**
- `CurrentPresetLabel`: 適用中プリセット名表示
- `PolicyLabel`: TurnPolicy 表示
- `ResearchModeCheckBox`: ResearchMode 連動

---

### NS-03 TurnPolicy（ターン制御ポリシー） ✅ 実装済み

**実装クラス:** `DebateModels.cs`, `AutoDebateService.cs`

| TurnPolicy | 内容 | 実装状況 |
|---|---|---|
| RoundRobin | 左右交互に発言 | ✅ |
| ModeratorSelect | フェーズ制（司会選択を模倣） | ✅ |
| HumanPriority | 人間介入優先 | ⬜ 将来実装 |
| CritiqueThenRefine | 提案→批判→改善の 3 フェーズ循環 | ✅ |
| ResearchReviewLoop | 仮説→証明→反例→査読の 4 フェーズ循環 | ✅ |

**フェーズ制の動作:**
- `phaseIndex` でフェーズを管理
- `GetRoleLabel()` でフェーズに対応したロールラベルを生成
- ロールラベルは転送テキストのヘッダに付加: `[Turn 3 Gemini→Perplexity (批判)]`

---

### NS-04 AutoDebateConfig 設定項目 ✅ 実装済み

**実装クラス:** `AutoDebateConfig.cs`

```csharp
WebView2  LeftWebView
WebView2  RightWebView
string    LeftUrl
string    RightUrl
bool      RequireApproval
ApprovalPolicy ApprovalPolicy
bool      AppendBridge
int       MaxTurns             // 往復数（0=無制限）
int       TurnIntervalMs       // ターン間インターバル（デフォルト 500ms）
int       PostSendWaitMs       // 送信後待機時間（デフォルト 5,000ms）
int       GenerationTimeoutMs  // 生成タイムアウト（デフォルト 90,000ms）
TurnPolicy TurnPolicy
bool      ResearchMode
ThirdSeatConfig ThirdSeat
string    LeftSystemPrompt
string    RightSystemPrompt
string    Topic
```

---

### NS-05 LoopDetector ✅ 実装済み
同一または類似テキストの繰り返しを検知して討論を停止する。

**実装クラス:** `LoopDetector.cs`

**実装詳細:**
- 直近 N 件のテキストをハッシュで保持
- 完全一致または類似度閾値超過で `IsLoop=true` を返す
- `AutoDebateService` から各ターンで呼び出し

---

### NS-06 連続失敗ガード ✅ 実装済み
テキスト未検出・送信失敗が `MaxConsecutiveFail`（= 3）回連続した場合に討論を自動停止する。

**実装場所:** `AutoDebateService.RunLoopAsync`

---

### NS-07 BoolToVisibilityConverter ✅ 実装済み
WPF バインディング用の `bool → Visibility` 変換コンバーター。

**実装クラス:** `BoolToVisibilityConverter.cs`

---

## 非機能要件

### NFR-01 拡張性
新しい AI サイト、新ロール、新討論モードを既存コードへの影響を小さく追加できる構造であること。
- アダプタは `BaseAiSiteAdapter` を継承し `AiSiteAdapterResolver` に登録するだけで追加可能。

### NFR-02 追跡性
すべての送受信、承認、引用、失敗イベントをログ化し、監査可能であること。

### NFR-03 可観測性
UI から現在のターン (`TurnCountLabel`)、発言者、承認待ち (`ApprovalPanel`)、生成中、停止理由を確認できること。

### NFR-04 安定性
- DOM 変更やサイト不整合時に graceful degradation
- テキスト未検出時のリトライ（最大 3 回）
- タイムアウト時の強制停止

### NFR-05 操作性
各発言に 1 行要約と送信先を持ち、ターンログから全文プレビュー可能であること。

### NFR-06 再現性
設定、プリセット、発話履歴を保存し、セッション再現が可能であること（JSON エクスポートで対応）。

---

## 参加者モデル

### 参加者属性
| 項目 | 内容 |
|---|---|
| participantId | 一意識別子 |
| displayName | 表示名 |
| participantType | Human / AiSite |
| role | Moderator / Debater / Critic / Refiner / Reviewer / Researcher |
| seat | Left / Right / Third / Virtual |
| controlMode | Manual / Approval / SemiAuto / FullAuto |
| siteAdapterId | AI サイト利用時のアダプタ ID |
| promptProfileId | 役割テンプレート ID |
| approvalPolicyId | 承認ルール ID |

### 制御モード
- **Manual**: 人間が毎回入力する。
- **Approval**: 下書きは自動生成し、送信前に承認する。
- **SemiAuto**: 条件付き承認で大半は自動送信する。
- **FullAuto**: 完全自動で往復する（現在のデフォルト）。

---

## 画面設計

### メイン画面 ✅ 実装済み
- 左ペイン: AI サイト 1 WebView2
- 右ペイン: AI サイト 2 WebView2
- 第 3 席パネル（モード・役割・インターバル・名前・URL 設定）
- 右サイドバー: ターンログ (`TurnLogListBox`)
- 上部ツールバー: 開始・停止・一時停止/再開・介入ボタン
- ステータスバー: タイムスタンプ付きメッセージ
- 承認パネル (`ApprovalPanel`): 承認待ち時にインライン表示

### テキストプレビューウィンドウ ✅ 実装済み
- ターンログのアイテムクリックで表示
- 発言全文・要約・方向・引用ボタン

### 介入ウィンドウ ✅ 実装済み
- テキスト入力欄 + 送信先選択（左・右・両方）

### 承認ウィンドウ ✅ 実装済み
- 送信ドラフトを編集・承認・却下できる専用ウィンドウ（`ApprovalWindow.xaml`）
- メインウィンドウ内インライン承認パネルも実装済み

### プリセット選択ウィンドウ ✅ 実装済み
- ビルトインプリセット一覧から選択して適用

### ロール設定ウィンドウ ✅ 実装済み
- 左右の表示名・システムプロンプト・議題を編集

### 研究ノートウィンドウ ✅ 実装済み
- 研究タグ一覧をリスト表示

### 第3席ウィンドウ ✅ 実装済み
- Human モード時のポップアップ入力ウィンドウ

---

## 詳細設計

### アーキテクチャ
本システムは 4 層構造とする。

1. **Presentation Layer** ✅: WPF UI、WebView2 表示、設定画面、ログ画面。
2. **Browser Automation Layer** ✅: WebView2 初期化、スクリプト注入、DOM 取得、送信操作。
3. **Orchestration Layer** ✅: ターン進行、承認判定、役割テンプレート、停止条件。
4. **Persistence Layer** ✅: セッション保存、ログ保存、テンプレート、引用、エクスポート。

### コアコンポーネント実装状況
| コンポーネント | クラス名 | 状態 |
|---|---|---|
| WebViewHostService | `MainWindow` に内包 | ✅ |
| SiteAdapterManager | `AiSiteAdapterResolver` | ✅ |
| ConversationMonitor | `ConversationMonitor` | ✅ |
| ExtractionService | 各アダプタの `ExtractLatestAsync` | ✅ |
| DraftBuilder | `AutoDebateService.RunLoopAsync` に内包 | ✅ |
| ApprovalEngine | `ApprovalPolicy` + `ApprovalQueue` | ✅ |
| TurnOrchestrator | `AutoDebateService` | ✅ |
| SummaryService | `SummaryService` | ✅（1行のみ）|
| QuoteService | `QuoteService` | ✅ |
| SessionRepository | `SessionRepository` | ✅ |
| ExportService | `ExportService` | ✅ |
| LoopDetector | `LoopDetector` | ✅ |
| ResearchService | `ResearchService` | ✅（基本実装）|

### WebView2 通信設計
WebView2 では `ExecuteScriptAsync` によるスクリプト実行を主体とし、各アダプタが `BaseAiSiteAdapter.ExecScriptBoolAsync` / `ExecScriptStringAsync` を通じて実行する。

### サイトアダプタ設計（現行）
```
IAiSiteAdapter
- string SiteName
- bool CanHandle(string url)
- Task<bool> SetInputAsync(WebView2, string text)
- Task<bool> SendAsync(WebView2)
- Task<string> ExtractLatestAsync(WebView2)
- Task<bool> IsGeneratingAsync(WebView2)
- Task InjectObserverAsync(WebView2)  // 現在は各アダプタで実装（Perplexity は no-op）
```

### ターン制御設計（詳細）
```
RunLoopAsync ループ変数:
  turn:       総送信回数（左右合計）
  roundCount: 往復カウント（MaxTurns 判定に使用）
  phaseIndex: TurnPolicy のフェーズ管理
  direction:  DebateDirection.LeftToRight / RightToLeft

終了条件:
  - MaxTurns > 0 && roundCount >= MaxTurns（往復数到達）
  - CancellationToken キャンセル
  - 連続失敗 MaxConsecutiveFail 回
  - LoopDetector が同一テキストを検知
  - アダプタが null
  - 承認却下
```

### 承認エンジン設計
承認判定は participant 単位ではなくルール合成とする。

```
ApprovalPolicy.ShouldRequireApproval(text, hasQuote, isAfterRecovery) -> bool
  判定条件:
  - RequireApprovalAlways == true
  - RequireApprovalWhenQuoted && hasQuote
  - RequireApprovalForLongMessage && text.Length > LongMessageThreshold
  - RequireApprovalForCodeOrSpec && テキストにコード・仕様キーワードを含む
  - RequireApprovalAfterRecovery && isAfterRecovery
```

### 引用設計
```
QuoteReference
- QuoteId            : Guid
- SourceMessageId    : string
- SourceTurnNumber   : int
- SourceDirection    : string
- QuotedText         : string
- Type               : QuoteType (Full / Partial)
- CreatedAt          : DateTime
```

### 数学研究モード設計
研究モード ON 時、各ターンのテキストから `ResearchService.ExtractAndAdd()` でタグを抽出し `ResearchNoteWindow` に蓄積する。

**将来の拡張タグ（Phase 5 予定）:**
| タグ | 意味 |
|---|---|
| Proposition | 命題 |
| Definition | 定義 |
| Assumption | 仮定 |
| ProofIdea | 証明方針 |
| LemmaCandidate | 補題候補 |
| Counterexample | 反例候補 |
| Gap | 論理の穴 |
| Unverified | 未検証 |
| Derived | 導出済み |

---

## データモデル

### TransferRecord（実装済み）
```json
{
  "TurnNumber": 4,
  "Direction": "Gemini→Perplexity",
  "Text": "...",
  "Summary": "反例候補を提示",
  "MessageId": "msg-4-R",
  "QuotedMessageIds": ["msg-2-L"]
}
```

### DebatePreset（実装済み）
```json
{
  "Name": "数学研究",
  "Description": "...",
  "TurnPolicy": "ResearchReviewLoop",
  "LeftPrompt": "あなたは証明案を提示する数学者です。",
  "RightPrompt": "あなたは反例を探索する数学者です。",
  "ResearchMode": true
}
```

### AutoDebateConfig（実装済み）
```json
{
  "MaxTurns": 10,
  "TurnIntervalMs": 500,
  "PostSendWaitMs": 5000,
  "GenerationTimeoutMs": 90000,
  "AppendBridge": true,
  "RequireApproval": false,
  "TurnPolicy": "RoundRobin",
  "ResearchMode": false
}
```

### ApprovalPolicy（実装済み）
```json
{
  "RequireApprovalAlways": false,
  "RequireApprovalWhenQuoted": true,
  "RequireApprovalForLongMessage": true,
  "LongMessageThreshold": 1800,
  "RequireApprovalForCodeOrSpec": true,
  "RequireApprovalAfterRecovery": true
}
```

---

## 設定プリセット

### 建設的討論プリセット ✅
- TurnPolicy: CritiqueThenRefine
- ResearchMode: false

### アプリ設計プリセット ✅
- TurnPolicy: RoundRobin
- ResearchMode: false

### 数学研究プリセット ✅
- TurnPolicy: ResearchReviewLoop
- ResearchMode: true

### ソクラテス式問答プリセット ✅
- TurnPolicy: ModeratorSelect
- ResearchMode: false

### なりきり議論プリセット ✅
- TurnPolicy: RoundRobin
- ResearchMode: false

---

## エラー処理

### 想定エラーと対処
| エラー | 対処 | 実装状況 |
|---|---|---|
| DOM セレクタ不一致 | 複数セレクタフォールバック | ✅ |
| 生成完了未検知 | リトライ（最大 3 回） + タイムアウト | ✅ |
| 同一メッセージ無限ループ | LoopDetector で検知・停止 | ✅ |
| 送信ボタン未検出 | 複数セレクタ試行 + KeyboardEvent 送信 | ✅ |
| 連続失敗 | MaxConsecutiveFail 3 回で自動停止 | ✅ |
| 承認却下 | 討論停止 | ✅ |
| ログイン切れ / CAPTCHA | 手動介入要求（未自動対応） | ⬜ |
| OCR フォールバック | 将来実装 | ⬜ |

---

## セキュリティ・法務・運用留意点
外部 AI サイトの自動操作はサイトごとの利用規約、ログインポリシー、bot 対策、CAPTCHA、レート制限の影響を受ける。本システムは各サイトでの安定性を保証するものではなく、対応はアダプタ単位で管理する。人間承認ゲートは、重要操作に対する誤送信防止にも有効である。

---

## 開発ロードマップ

### Phase 0: 要件整理と技術検証 ✅ 完了
- WPF + WebView2 の基本画面作成
- 2 WebView 表示
- `ExecuteScriptAsync` による手動取得検証
- 監視スクリプトの quiet period 判定検証

### Phase 1: MVP ✅ 完了
- 左右 2 ペイン
- 手動送信、手動取得
- GeminiAdapter, PerplexityAdapter 実装
- ログ保存（TransferRecord, SessionRepository）
- 停止・再開ボタン

### Phase 2: 自動討論基盤 ✅ 完了
- 自動生成完了検知（ConversationMonitor）
- 自動取得（ExtractLatestAsync）
- 自動ドラフト生成（AutoDebateService.RunLoopAsync）
- 承認待ちキュー（ApprovalQueue）
- 1 行要約（SummaryService）
- ループ検知（LoopDetector）
- 連続失敗ガード
- MaxTurns 往復カウント修正

### Phase 3: 3 人型・引用・承認高度化 ✅ 完了
- 第 3 席導入（ThirdSeatConfig, ThirdSeatWindow）
- Human / AI 切替
- 全文引用（QuoteService, TextPreviewWindow）
- 条件付き承認ルール（ApprovalPolicy）
- プリセットシステム（DebatePreset, PresetSelectorWindow）
- ロール設定 UI（RoleSettingsWindow, PromptProfile）
- エクスポート（ExportService: Markdown / JSON / TXT）
- 途中介入（InterventionWindow）

### Phase 4: 汎用討論モード 🚧 一部完了
- ✅ 建設的討論プリセット
- ✅ なりきり議論プリセット
- ✅ アプリ設計プリセット
- ✅ TurnPolicy 実装（RoundRobin, CritiqueThenRefine, ResearchReviewLoop, ModeratorSelect）
- ✅ ロールラベル付きヘッダ転送
- ⬜ ロールテンプレート編集 UI（カスタムプリセット保存）
- ⬜ 司会サマリー（論点整理・合意点・対立点の自動生成）
- ⬜ HumanPriority TurnPolicy

### Phase 5: 数学・研究モード 🚧 基本実装済み
- ✅ 研究タグ抽出（ResearchService）
- ✅ 研究ノートウィンドウ（ResearchNoteWindow）
- ✅ ResearchMode ON/OFF 設定
- ⬜ 命題・定義・補題の構造化タグ（Proposition, Definition, LemmaCandidate 等）
- ⬜ 反例候補管理ビュー
- ⬜ 未証明点一覧
- ⬜ 厳密性チェック支援プロンプト

### Phase 6: 安定化・運用機能 🔄 継続
- ⬜ サイトアダプタ追加（ChatGPT, Claude 等）
- ⬜ OCR フォールバック
- ⬜ セッション再現（JSON からロード）
- ⬜ カスタムプリセット保存・読み込み UI
- ⬜ 部分引用（テキスト選択 → 引用登録）
- ⬜ ログイン切れ・CAPTCHA 検知と通知
- ⬜ パフォーマンス改善
- ⬜ UI/UX 改善（テーマ、フォント、レイアウト最適化）

---

## 優先順位

### Must Have ✅ 全実装済み
- WebView2 表示
- 自動取得
- 承認制送信
- ユーザー途中介入
- 引用返信（全文）
- ログ保存
- サイトアダプタ構造

### Should Have 🚧 一部実装済み
- ✅ 3 席構成
- ⬜ 司会サマリー（自動論点整理）
- ✅ 1 行要約
- ✅ 条件付き承認
- ✅ アプリ設計プリセット
- ✅ 研究モード（基本）

### Could Have ⬜ 未実装
- OCR フォールバック
- 自動採点
- dissent 保存
- 参加者数拡張（4席以上）
- 外部ファイル取り込み
- 部分引用 UI

---

## 実装技術
| 項目 | 採用 |
|---|---|
| UI | WPF (.NET 8) |
| 埋め込みブラウザ | Microsoft.Web.WebView2 |
| 言語 | C# |
| ローカル保存 | JSON ファイル（`%AppData%/gironWin/`）|
| ログ出力 | Markdown / JSON / TXT |
| フォールバック OCR | 未実装（将来: Windows OCR 検討）|
| ブラウザ DOM 操作 | ExecuteScriptAsync + TreeWalker |

---

## 初期マイルストーン達成状況
| # | マイルストーン | 状態 |
|---|---|---|
| 1 | 左右 2 WebView の表示と URL 読み込み完了 | ✅ |
| 2 | 1 サイトで最新発言抽出成功 | ✅ |
| 3 | 取得文を反対側へ自動入力成功 | ✅ |
| 4 | 承認待ちキューから送信成功 | ✅ |
| 5 | 第 3 席 Human 介入成功 | ✅ |
| 6 | 引用返信成功（全文） | ✅ |
| 7 | 建設的討論プリセット完成 | ✅ |
| 8 | 数学研究プリセット完成（基本） | 🚧 |

---

## 完成イメージ
完成版は、AI チャットサイトを横断して議論を自動進行しつつ、人間が途中で理解・介入・承認・引用・修正できる、汎用的な討論・設計・研究ワークベンチとなる。multi-agent debate の利点である複数視点と、human-in-the-loop の強みである判断責任・追跡性・承認を両立することが、このプロダクトの本質である。
