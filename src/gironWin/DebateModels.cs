using System.Collections.Generic;

namespace gironWin
{
    public enum TurnPolicy
    {
        RoundRobin,
        ModeratorSelect,
        HumanPriority,
        CritiqueThenRefine,
        ResearchReviewLoop
    }

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
        public string     Description  { get; set; } = string.Empty;
    }

    public sealed class ThirdSeatConfig
    {
        public ThirdSeatMode Mode          { get; set; } = ThirdSeatMode.Disabled;
        public DebateRole    Role          { get; set; } = DebateRole.Moderator;
        public string        DisplayName   { get; set; } = "\u7b2c3\u5e2d";
        public int           IntervalTurns { get; set; } = 2;
        public string        Url           { get; set; } = string.Empty;

        // AutoDebateService AiSite \u30e2\u30fc\u30c9\u7528\u62e1\u5f35
        public Microsoft.Web.WebView2.Wpf.WebView2? WebView      { get; set; }
        public string                               StaticText   { get; set; } = string.Empty;
        public string                               SystemPrompt { get; set; } = string.Empty;
    }

    // FR-13: \u7814\u7a76\u30bf\u30b0\u30a8\u30f3\u30c8\u30ea
    public sealed class ResearchTagEntry
    {
        public string TagType    { get; set; } = string.Empty; // Proposition/Definition/etc
        public string Content    { get; set; } = string.Empty;
        public int    TurnNumber { get; set; }
        public string MessageId  { get; set; } = string.Empty;
    }

    // FR-10: \u5f15\u7528\u30e2\u30c7\u30eb
    public sealed class QuoteReference
    {
        public string QuoteId             { get; set; } = System.Guid.NewGuid().ToString();
        public string SourceMessageId     { get; set; } = string.Empty;
        public string SourceParticipantId { get; set; } = string.Empty;
        public int    SourceTurnNumber    { get; set; }
        public int    StartIndex          { get; set; }
        public int    EndIndex            { get; set; }
        public string QuotedText          { get; set; } = string.Empty;
        public string QuoteType           { get; set; } = "Full"; // Full / Partial
    }

    // FR-07: \u7b2c3\u5e2d\u5165\u529b\u30ea\u30af\u30a8\u30b9\u30c8
    public sealed class ThirdSeatInputRequest
    {
        public int    TurnNumber   { get; set; }
        public string Context      { get; set; } = string.Empty;
        public string Role         { get; set; } = string.Empty;
        public System.Action<string>? OnSubmit { get; set; }
    }
}
