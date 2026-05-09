# AI討論ワークベンチ 要件定義書・詳細設計・開発ロードマップ

## 概要
本書は、Windows 上で複数の AI チャットサイトを専用ブラウザのように表示し、生成文章を自動取得・相互受け渡し・議論進行・人間介入・引用管理できる「AI討論ワークベンチ」の要件定義書、詳細設計方針、および開発ロードマップをまとめたものである。WebView2 は Windows デスクトップアプリへ Edge ベースの Web コンテンツを埋め込め、JavaScript 実行やページとのメッセージ連携が可能であるため、本システムの基盤として有力である[cite:13][cite:16][cite:33][cite:34]。

本システムは単なる自動往復ツールではなく、human-in-the-loop の承認ゲート、途中介入、引用返信、役割ベース討論、研究用途向けの厳密性チェックを備えた、汎用的な議論オーケストレーション基盤を目指す。人間参加型ワークフローは、重要な判断の前で承認を入れたり、途中で介入可能にしたりする設計が中核とされる[cite:73][cite:76][cite:79][cite:84]。

## プロダクト定義
### 目的
ユーザーが指定した議題、立場、役割、研究テーマ、設計テーマに対して、複数の AI チャットサイトおよび人間参加者を組み合わせ、建設的・批判的・査読的・数学的・創造的な議論を継続的に行えるデスクトップアプリを提供する。Multi-agent debate や deliberative prompting では、役割分担、モデレーター、批判者、改善者のような構成が議論の質を高める方向で用いられている[cite:45][cite:47][cite:49][cite:68]。

### プロダクトの核
本プロダクトの核は次の 6 点である。

- 複数 AI サイトを同時表示し、発言を自動取得するブラウザ操作基盤[cite:13][cite:16][cite:17]
- 発言を相手へ自動または承認付きで送る討論オーケストレーター[cite:76][cite:81][cite:84]
- ユーザーが司会・討論者・査読者として参加できる 3 席以上の人間参加型設計[cite:63][cite:67][cite:73]
- 過去ログからの全文・部分引用と、引用元追跡機能[cite:80][cite:83][cite:86]
- 数学研究や設計レビューに対応する厳密性・反例・未解決点の構造化[cite:52][cite:54][cite:67]
- 承認制、停止条件、ログ記録、エクスポートを含む運用安全性[cite:76][cite:79][cite:84]

## 想定ユースケース
### 主要ユースケース
| ユースケース | 内容 | 必要機能 |
|---|---|---|
| 建設的討論 | AI 同士が案を出し、批判し、改善する | 役割設定、受け渡しテンプレート、司会要約 [cite:49] |
| ユーザー司会 | ユーザーが途中で論点整理し次の問いを投げる | 途中介入、手動司会ターン、承認待ち [cite:73][cite:76] |
| ユーザー討論参加 | ユーザーが片側として発言し、AI が対戦する | 人間席、入力欄、送信制御 [cite:63][cite:73] |
| アプリ設計レビュー | 要件、仕様、UI、リスクを AI と議論する | 引用、承認、成果物化 [cite:77][cite:78] |
| 数学研究 | 命題、証明案、反例候補、未解決点を整理する | 厳密性タグ、反例探索、構造化ログ [cite:52][cite:54][cite:67] |
| 査読モード | 文書・論文・設計に対する批判と改善 | 査読役、改善役、要約役 [cite:47][cite:49] |

### ペルソナ
- 高度な開発者、研究者、企画担当者、仕様策定者、教育用途の利用者を主対象とする。
- AI を単発利用するのではなく、複数視点を競合させながら品質を上げたいユーザーを想定する[cite:45][cite:49][cite:69]。

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

## 機能要件
### FR-01 マルチペイン表示
システムは最低 3 席の参加者概念を持ち、左右 2 つの WebView2 と、第 3 席の Human / AI 切替パネルを提供する。WebView2 はデスクトップアプリ内にブラウザを埋め込み、JavaScript 実行や通信連携に利用できる[cite:13][cite:16][cite:33]。

### FR-02 サイト別アダプタ
システムは AI サイトごとに入力欄、送信ボタン、メッセージ抽出位置、生成中判定、エラー処理を持つアダプタを提供する。DOM 構造はサイトごとに異なり変化しやすいため、サイト別アダプタ分離が必要である[cite:7][cite:17][cite:36]。

### FR-03 自動生成完了検知
システムはページへ注入した監視スクリプトにより DOM 変化を監視し、一定時間テキストが増加しない、生成中 UI が消えるなどの条件を満たしたとき生成完了と判定する。MutationObserver は DOM 変化追跡に用いられ、WebView2 ではスクリプト注入とメッセージ通知が可能である[cite:17][cite:33][cite:34][cite:42]。

### FR-04 自動取得と保存
システムは最新発言、全文ログ、1 行要約、引用可能な段落単位テキストを自動保存する。保存データは後から引用・再送・成果物生成に再利用できるよう構造化されなければならない[cite:83][cite:86]。

### FR-05 相互受け渡し
システムは片側の発言を整形し、橋渡し文を付加して相手側の入力欄へ自動入力できる。建設的議論では単純転送ではなく、役割と目的を埋め込んだ受け渡しテンプレートが有効である[cite:49][cite:68][cite:71]。

### FR-06 役割・人格・視点設定
各席は参加者タイプと議論ロールを分離して設定できる。role-aware な設計は、提案役・批判役・改善役・司会役・査読役などの分業に適している[cite:48][cite:49]。

### FR-07 人間参加
各席には Human または AI を割り当てできる。ユーザーは司会、討論者、査読者、観察者として途中参加できる human-in-the-loop 設計を必須とする[cite:63][cite:67][cite:73]。

### FR-08 途中介入
システムは各ターン終了時、司会ターン後、エラー発生時、一定ターン経過時などの条件でユーザー介入ポイントを設ける。重要なアクション前の人間確認は HITL ワークフローの基本である[cite:76][cite:79][cite:84]。

### FR-09 承認制送信
送信は participant 単位・イベント単位・条件単位で承認制をオンオフ可能とする。承認ノードや approval gate は high-risk action に対する制御として用いられる[cite:81][cite:84][cite:85]。

### FR-10 引用返信
ユーザーおよび AI は、過去の全文または部分テキストを引用して返信内容に含められる。引用返信は文脈の明確化と追跡性向上に有効である[cite:80][cite:83][cite:86]。

### FR-11 ログ追跡
各発言には messageId、turnNumber、participantId、rawText、summary、quotedMessageIds、approvalStatus、deliveryTarget、timestamps を保持する。これにより、どの発言がどの議論へ影響したかを追跡できる。

### FR-12 司会支援
司会役は各ターンで論点整理、合意点、対立点、未解決点、次の問いを生成できる。民主的対話支援や deliberation では共通 ground の整理が有用とされる[cite:66][cite:69]。

### FR-13 数学・研究モード
システムは命題、定義、仮定、証明案、反例候補、未証明点、要検証補題を構造化して扱える。multi-agent debate は数学 reasoning でも応用されるが、失敗モードがあるため厳密性確認が必要である[cite:52][cite:54][cite:67]。

### FR-14 成果物生成
議論終了後に合意点、対立点、引用根拠、次アクション、仕様案、研究ノートを生成できる。構造化 ideation や review の最終出力化は多エージェント設計の重要用途である[cite:77][cite:78]。

## 非機能要件
### NFR-01 拡張性
新しい AI サイト、新ロール、新討論モードを既存コードへの影響を小さく追加できる構造であること。

### NFR-02 追跡性
すべての送受信、承認、引用、編集、失敗イベントをログ化し、監査可能であること。承認付きワークフローではイベント履歴が重要である[cite:76][cite:81]。

### NFR-03 可観測性
UI から現在のターン、発言者、承認待ち、生成中、停止理由、引用元を確認できること。

### NFR-04 安定性
DOM 変更やサイト不整合時に graceful degradation し、再取得、手動介入、OCR フォールバックへ移行できること。OCR によるテキスト抽出は画面ベースのフォールバックとして実用性がある[cite:3]。

### NFR-05 操作性
長い議論でもユーザーが流れを追えるよう、各発言に 1 行要約、引用リンク、送信予定プレビューを持つこと。

### NFR-06 再現性
設定、テンプレート、停止条件、参加者構成、発話履歴を保存し、同一セッション再現が可能であること。

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
- Manual: 人間が毎回入力する。
- Approval: 下書きは自動生成し、送信前に承認する。
- SemiAuto: 条件付き承認で大半は自動送信する。
- FullAuto: 完全自動で往復する。

## 画面設計
### メイン画面
- 左ペイン: AI サイト 1 表示
- 右ペイン: AI サイト 2 表示
- 下部または中央: 第 3 席パネル
- 右サイドバー: ターンログ、引用元、承認待ち、停止条件、エラー
- 上部ツールバー: 開始、停止、一時停止、次ターン、手動介入、プリセット切替

### 発言カード
各発言カードは次を持つ。

- 発言者名
- ロール
- ターン番号
- 生テキスト
- 1 行要約
- 引用元一覧
- 承認状態
- 送信先
- 操作ボタン: 引用、部分引用、編集、承認、却下、再送、要約再生成

### 承認待ちキュー
送信前ドラフトを一覧表示し、承認・修正・差し戻し・今回のみ自動送信・今後自動送信へ変更などを操作できる。

## 詳細設計
### アーキテクチャ
本システムは 4 層構造とする。

1. Presentation Layer: WPF UI、WebView2 表示、設定画面、ログ画面。
2. Browser Automation Layer: WebView2 初期化、スクリプト注入、DOM 取得、送信操作、WebMessage 受信[cite:16][cite:33][cite:34]。
3. Orchestration Layer: ターン進行、承認判定、役割テンプレート、停止条件、司会支援[cite:49][cite:68][cite:71]。
4. Persistence Layer: セッション保存、ログ保存、テンプレート、引用、エクスポート。

### コアコンポーネント
| コンポーネント | 責務 |
|---|---|
| WebViewHostService | 各 WebView2 の生成・初期化・ナビゲーション |
| SiteAdapterManager | URL に応じたアダプタ選択 |
| ConversationMonitor | MutationObserver 通知受信、生成完了判定 |
| ExtractionService | 最新メッセージとログ抽出 |
| DraftBuilder | 橋渡し文、ロール文、引用文を含む送信ドラフト生成 |
| ApprovalEngine | 承認要否判定、待ちキュー投入 |
| TurnOrchestrator | 次発言者決定、送信順制御 |
| SummaryService | 1 行要約、論点整理、終了要約 |
| QuoteService | 全文・部分引用の管理 |
| SessionRepository | セッション永続化 |
| ExportService | Markdown / JSON / txt 出力 |

### WebView2 通信設計
WebView2 では JavaScript 実行とネイティブ側との相互運用に `ExecuteScriptAsync`、`WebMessageReceived`、`chrome.webview.postMessage(...)` が利用できる[cite:16][cite:33][cite:34]。そのため、各ページには起動時に監視スクリプトを注入する。

#### 注入スクリプトの役割
- 対象メッセージ DOM の監視
- 文字列長変化の監視
- 生成中 UI の観測
- quiet period 判定
- 最新メッセージ抽出
- .NET 側への postMessage 通知

### サイトアダプタ設計
```text
IAiSiteAdapter
- bool CanHandle(Uri url)
- Task InitializeAsync(WebViewHandle handle)
- Task InjectObserverAsync(WebViewHandle handle)
- Task<bool> IsReadyAsync(WebViewHandle handle)
- Task<MessageExtractionResult> ExtractLatestAsync(WebViewHandle handle)
- Task<IReadOnlyList<MessageBlock>> ExtractAllAsync(WebViewHandle handle)
- Task SetInputAsync(WebViewHandle handle, string text)
- Task ClickSendAsync(WebViewHandle handle)
- Task<bool> IsGeneratingAsync(WebViewHandle handle)
- Task<UiHints> GetUiHintsAsync(WebViewHandle handle)
```

### ターン制御設計
ターン制御は固定左右往復ではなくポリシー駆動とする。

| TurnPolicy | 内容 |
|---|---|
| RoundRobin | 順番に発言 |
| ModeratorSelect | 司会が次発言者を決定 |
| HumanPriority | 人間介入要求がある場合は優先 |
| CritiqueThenRefine | 提案 → 批判 → 改善 |
| ResearchReviewLoop | 仮説 → 証明案 → 反例 → 査読 |

### 承認エンジン設計
承認判定は participant 単位ではなくルール合成とする。

#### 判定条件例
- participant の `RequireApprovalBeforeSend = true`
- メッセージ長が閾値超過
- 引用数が多い
- コード、仕様、証明、数式が含まれる
- エラー復帰後の初回送信
- 同一率が高いメッセージ
- 司会が重要ターンと判断

### 引用設計
引用は全文引用と部分引用を持つ。

#### 引用モデル
```text
QuoteReference
- quoteId
- sourceMessageId
- sourceParticipantId
- sourceTurnNumber
- startIndex
- endIndex
- quotedText
- quoteType (Full / Partial)
- createdAt
```

#### 振る舞い
- 発言カードから全文引用
- テキスト選択から部分引用
- 引用を含む送信文をプレビュー
- 送信文に引用元メタ情報を保持
- 引用元へジャンプ

### 数学研究モード設計
数学モードでは通常の議論に加え、内容を構造化タグで保持する。

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

司会役または査読役は毎ターン、どこが厳密に確定し、どこが未証明かを整理する。数学的議論では失敗モードを避けるため、結論より検証状態の明示が重要である[cite:52][cite:54][cite:67]。

## データモデル
### Message
```json
{
  "messageId": "msg-001",
  "sessionId": "sess-001",
  "turnNumber": 4,
  "participantId": "left-ai",
  "role": "Critic",
  "rawText": "...",
  "summary": "反例候補を提示",
  "quotedMessageIds": ["msg-002"],
  "approvalStatus": "Approved",
  "deliveryTarget": ["right-ai"],
  "createdAt": "2026-05-09T18:00:00+09:00"
}
```

### ParticipantConfig
```json
{
  "participantId": "right-ai",
  "participantType": "AiSite",
  "role": "Moderator",
  "seat": "Right",
  "controlMode": "Approval",
  "siteAdapterId": "chatgpt-web",
  "promptProfileId": "moderator-v1",
  "approvalPolicyId": "approval-long-or-code"
}
```

### ApprovalPolicy
```json
{
  "approvalPolicyId": "approval-long-or-code",
  "requireApprovalBeforeSend": false,
  "requireApprovalWhenQuoted": true,
  "requireApprovalForLongMessage": true,
  "longMessageThreshold": 1800,
  "requireApprovalForCodeOrSpec": true,
  "requireApprovalAfterRecovery": true
}
```

## 設定プリセット
### 建設的討論プリセット
- 左: 提案役、FullAuto
- 右: 批判役、Approval
- 第 3 席: ユーザー司会、Manual
- TurnPolicy: CritiqueThenRefine

### アプリ設計プリセット
- 左: 実装担当 AI
- 右: アーキテクト兼リスク指摘 AI
- 第 3 席: ユーザー PO
- 重要仕様・コード提案は承認制

### 数学研究プリセット
- 左: 証明案 AI
- 右: 反例探索 AI
- 第 3 席: ユーザー研究者または査読 AI
- TurnPolicy: ResearchReviewLoop
- すべての補題候補に Unverified タグ付与初期化

## エラー処理
### 想定エラー
- ログイン切れ
- DOM セレクタ不一致
- 送信ボタン未検出
- 生成完了未検知
- 同一メッセージ無限ループ
- CAPTCHA や利用制限
- ネットワークエラー

### 対処方針
- リトライ
- 手動介入要求
- サイトアダプタ切替
- OCR フォールバック[cite:3]
- 強制停止
- セッション保存後終了

## セキュリティ・法務・運用留意点
外部 AI サイトの自動操作はサイトごとの利用規約、ログインポリシー、bot 対策、CAPTCHA、レート制限の影響を受ける。したがって、本システムは各サイトでの安定性を保証するものではなく、対応はアダプタ単位で管理する必要がある。人間承認ゲートは、重要操作に対する誤送信防止にも有効である[cite:76][cite:84]。

## 開発ロードマップ
### Phase 0: 要件整理と技術検証（1〜2 週間）
- WPF + WebView2 の基本画面作成
- 2 WebView 表示
- 1 サイトで `ExecuteScriptAsync` による手動取得検証[cite:16]
- `WebMessageReceived` による JS → .NET 通知検証[cite:34]
- 監視スクリプトの quiet period 判定検証[cite:17][cite:42]

### Phase 1: MVP（2〜4 週間）
- 左右 2 ペイン
- 手動送信、手動取得
- 1 サイトアダプタ実装
- ログ保存
- 引用なしの単純受け渡し
- 停止・再開ボタン

### Phase 2: 自動討論基盤（3〜5 週間）
- 自動生成完了検知
- 自動取得
- 自動ドラフト生成
- 片側自動送信
- 承認待ちキュー
- 1 行要約
- ループ検知

### Phase 3: 3 人型・引用・承認高度化（3〜5 週間）
- 第 3 席導入
- Human / AI 切替
- 全文引用
- 部分引用
- participant 単位承認設定
- 条件付き承認ルール
- 司会サマリー

### Phase 4: 汎用討論モード（3〜6 週間）
- 建設的討論プリセット
- なりきり議論プリセット
- 査読プリセット
- アプリ設計プリセット
- TurnPolicy 実装
- ロールテンプレート編集 UI

### Phase 5: 数学・研究モード（4〜6 週間）
- 命題・定義・補題タグ
- 反例候補管理
- 厳密性チェック支援
- 未証明点一覧
- 研究ノート出力

### Phase 6: 安定化・運用機能（継続）
- サイトアダプタ追加
- OCR フォールバック
- 高度なエクスポート
- セッション再現
- パフォーマンス改善
- UI/UX 改善

## 優先順位
### Must Have
- WebView2 表示
- 自動取得
- 承認制送信
- ユーザー途中介入
- 引用返信
- ログ保存
- サイトアダプタ構造

### Should Have
- 3 席構成
- 司会サマリー
- 1 行要約
- 条件付き承認
- アプリ設計プリセット
- 研究モード

### Could Have
- OCR フォールバック
- 自動採点
- dissent 保存
- 参加者数拡張
- 外部ファイル取り込み

## 実装技術候補
| 項目 | 推奨 |
|---|---|
| UI | WPF |
| 埋め込みブラウザ | WebView2 [cite:13][cite:16] |
| 言語 | C# / .NET |
| ローカル保存 | SQLite + JSON |
| ログ出力 | Markdown / JSON |
| フォールバック OCR | Windows OCR / 外部 OCR 検討 [cite:3] |

## 初期マイルストーン
1. 左右 2 WebView の表示と URL 読み込み完了。
2. 1 サイトで最新発言抽出成功。
3. 取得文を反対側へ自動入力成功。
4. 承認待ちキューから送信成功。
5. 第 3 席 Human 介入成功。
6. 引用返信成功。
7. 建設的討論プリセット完成。
8. 数学研究プリセット完成。

## 完成イメージ
完成版は、AI チャットサイトを横断して議論を自動進行しつつ、人間が途中で理解・介入・承認・引用・修正できる、汎用的な討論・設計・研究ワークベンチとなる。multi-agent debate の利点である複数視点と、human-in-the-loop の強みである判断責任・追跡性・承認を両立することが、このプロダクトの本質である[cite:45][cite:63][cite:67][cite:76]。
