using Microsoft.Web.WebView2.Wpf;

namespace gironWin
{
    /// <summary>
    /// FR-01 Phase 3: 第3席の設定
    /// </summary>
    public enum ThirdSeatMode
    {
        /// <summary>第3席を使わない</summary>
        Disabled,
        /// <summary>人間が手入力</summary>
        Human,
        /// <summary>AI サイトを埋め込み</summary>
        AiSite
    }

    public sealed class ThirdSeatConfig
    {
        public ThirdSeatMode Mode     { get; set; } = ThirdSeatMode.Disabled;
        public DebateRole    Role     { get; set; } = DebateRole.Moderator;
        public string        DisplayName { get; set; } = "第3席";

        /// <summary>AiSite モード時の WebView2</summary>
        public WebView2?     WebView  { get; set; }
        /// <summary>AiSite モード時の URL</summary>
        public string        Url      { get; set; } = string.Empty;
        /// <summary>Human モード時の固定テキスト（空なら毎ターン入力を待つ）</summary>
        public string        StaticText { get; set; } = string.Empty;
        /// <summary>第3席のシステムプロンプト</summary>
        public string        SystemPrompt { get; set; } = string.Empty;

        /// <summary>何ターンごとに第3席を挟むか（0=挟まない, 1=毎ターン, 2=2ターンごと）</summary>
        public int           IntervalTurns { get; set; } = 2;
    }
}
