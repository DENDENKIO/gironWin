using Microsoft.Web.WebView2.Wpf;

namespace gironWin
{
    /// <summary>
    /// 自動討論の設定。Phase 3-5 拡張版。
    /// </summary>
    public sealed class AutoDebateConfig
    {
        // 基本
        public WebView2 LeftWebView  { get; set; } = null!;
        public WebView2 RightWebView { get; set; } = null!;
        public string   LeftUrl      { get; set; } = string.Empty;
        public string   RightUrl     { get; set; } = string.Empty;
        public bool     AppendBridge    { get; set; }
        public bool     RequireApproval { get; set; } = true;
        public int      MaxTurns        { get; set; }
        public int      TurnIntervalMs  { get; set; } = 500;
        public int      GenerationTimeoutMs { get; set; } = 90000;
        public int      PostSendWaitMs      { get; set; } = 5000;
        public string   Topic { get; set; } = string.Empty;

        // FR-06: 役割プロンプト
        public string LeftSystemPrompt  { get; set; } = string.Empty;
        public string RightSystemPrompt { get; set; } = string.Empty;

        // Phase 3: 第3席
        public ThirdSeatConfig ThirdSeat { get; set; } = new();

        // Phase 4: ターンポリシー
        public TurnPolicy TurnPolicy { get; set; } = TurnPolicy.RoundRobin;

        // Phase 5: 研究モード
        public bool ResearchMode { get; set; }

        // NFR-02: 承認ポリシー（null の場合は ApprovalPolicy.Default を使用）
        public ApprovalPolicy? ApprovalPolicy { get; set; }
    }
}
