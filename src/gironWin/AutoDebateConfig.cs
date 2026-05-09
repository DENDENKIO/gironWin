using Microsoft.Web.WebView2.Wpf;

namespace gironWin
{
    /// <summary>
    /// 自動討論の設定。
    /// FR-06 役割プロンプトを追加。
    /// </summary>
    public sealed class AutoDebateConfig
    {
        public WebView2 LeftWebView  { get; set; } = null!;
        public WebView2 RightWebView { get; set; } = null!;
        public string   LeftUrl      { get; set; } = string.Empty;
        public string   RightUrl     { get; set; } = string.Empty;
        public bool     AppendBridge    { get; set; }
        public bool     RequireApproval { get; set; } = true;
        public int      MaxTurns        { get; set; }
        public int      TurnIntervalMs  { get; set; } = 500;
        public int      GenerationTimeoutMs { get; set; } = 90000;

        /// <summary>FR-06: 左席 AI に付加するシステムプロンプト</summary>
        public string LeftSystemPrompt  { get; set; } = string.Empty;
        /// <summary>FR-06: 右席 AI に付加するシステムプロンプト</summary>
        public string RightSystemPrompt { get; set; } = string.Empty;
    }
}
