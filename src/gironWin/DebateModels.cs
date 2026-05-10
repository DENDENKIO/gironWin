using System.Collections.Generic;

namespace gironWin
{

    public enum DebateRole
    {
        Debater,
        Moderator,
        Critic,
        Refiner,
        Reviewer,
        Researcher
    }

    public enum ThirdSeatMode
    {
        Disabled,
        Human,
        AiSite
    }

    public sealed class DebatePreset
    {
        public string     Name         { get; set; } = string.Empty;
        public TurnPolicy TurnPolicy   { get; set; } = TurnPolicy.RoundRobin;
        public bool       ResearchMode { get; set; }
        public string     LeftPrompt   { get; set; } = string.Empty;
        public string     RightPrompt  { get; set; } = string.Empty;
        public string     Topic        { get; set; } = string.Empty;
        public string     Description  { get; set; } = string.Empty;
    }

    public sealed class ThirdSeatConfig
    {
        public ThirdSeatMode Mode          { get; set; } = ThirdSeatMode.Disabled;
        public DebateRole    Role          { get; set; } = DebateRole.Moderator;
        public string        DisplayName   { get; set; } = "\u7b2c3\u5e2d";
        public int           IntervalTurns { get; set; } = 2;
        public string        Url           { get; set; } = string.Empty;

        // AutoDebateService AiSite モード用拡張
        public Microsoft.Web.WebView2.Wpf.WebView2? WebView      { get; set; }
        public string                               StaticText   { get; set; } = string.Empty;
        public string                               SystemPrompt { get; set; } = string.Empty;
    }

    // FR-10: \u5f15\u7528\u30e2\u30c7\u30eb

    // FR-07: \u7b2c3\u5e2d\u5165\u529b\u30ea\u30af\u30a8\u30b9\u30c8
}
