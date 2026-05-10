using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace gironWin.Shared
{
    // ─────────────────────────────────────
    // TransferRecord（元のまま）
    // ─────────────────────────────────────
    public static class ApprovalStatuses
    {
        public const string Pending  = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
    }

    public sealed class TransferRecord : INotifyPropertyChanged
    {
        private string _approvalStatus = ApprovalStatuses.Pending;

        public int      TurnNumber         { get; set; }
        public string   Direction          { get; set; } = string.Empty;
        public string   Text               { get; set; } = string.Empty;
        public DateTime Timestamp          { get; set; } = DateTime.Now;
        public string   TimestampText      => Timestamp.ToString("HH:mm:ss");
        public string   SourceSite         { get; set; } = string.Empty;
        public string   TargetSite         { get; set; } = string.Empty;
        public bool     Submitted          { get; set; }
        public string   Status             { get; set; } = string.Empty;
        public string   ParticipantRole    { get; set; } = string.Empty;
        public string   SessionId          { get; set; } = string.Empty;
        public string   MessageId          { get; set; } = Guid.NewGuid().ToString();
        public string   Summary            { get; set; } = string.Empty;
        public string   MessageLogEntryId  { get; set; } = string.Empty;
        public List<string> QuotedMessageIds { get; set; } = new();
        public string   DeliveryTarget     { get; set; } = string.Empty;
        public List<ResearchTagEntry> ResearchTags { get; set; } = new();
        public List<string> Tags           { get; set; } = new();
        public bool     HasTags            => Tags.Count > 0;
        public List<ParagraphBlock> ParagraphBlocks { get; set; } = new();
        public string? HtmlSnapshotPath { get; set; }
        public string? InputText        { get; set; } // ★ 追加: AIに送ったプロンプト

        public string ApprovalStatus
        {
            get => _approvalStatus;
            set
            {
                if (_approvalStatus == value) return;
                _approvalStatus = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ApprovalStatus)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ApprovalStatusDisplay)));
            }
        }

        public string ApprovalStatusDisplay => _approvalStatus switch
        {
            ApprovalStatuses.Approved => "✅",
            ApprovalStatuses.Rejected => "❌",
            _                         => "⏳"
        };

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public sealed class ParagraphBlock
    {
        public int    Index     { get; set; }
        public int    CharStart { get; set; }
        public int    CharEnd   { get; set; }
        public string Text      { get; set; } = string.Empty;
    }

    // ─────────────────────────────────────
    // ResearchTagEntry
    // ─────────────────────────────────────
    public sealed class ResearchTagEntry
    {
        public string   TagType     { get; init; } = string.Empty;
        public string   SubTagType  { get; init; } = string.Empty;
        public string   Text        { get; init; } = string.Empty;
        public int      TurnNumber  { get; init; }
        public string   MessageId   { get; init; } = string.Empty;
        public DateTime ExtractedAt { get; init; } = DateTime.Now;
        public int      Importance  { get; init; } = 1;

        public string DisplayTag =>
            string.IsNullOrWhiteSpace(SubTagType)
                ? TagType
                : $"{TagType} / {SubTagType}";

        public string ImportanceLabel => Importance switch
        {
            3 => "🔴 高",
            2 => "🟡 中",
            _ => "⚪ 低"
        };
    }

    public static class ResearchTagTypes
    {
        public const string Proposition    = "Proposition";
        public const string Definition     = "Definition";
        public const string Assumption     = "Assumption";
        public const string ProofIdea      = "ProofIdea";
        public const string LemmaCandidate = "LemmaCandidate";
        public const string Counterexample = "Counterexample";
        public const string Gap            = "Gap";
        public const string Unverified     = "Unverified";
        public const string Derived        = "Derived";
        public const string OpenQuestion   = "OpenQuestion";
        public const string Rigor          = "Rigor";
        public const string Agreement      = "Agreement";
        public const string Disagreement   = "Disagreement";
    }

    // ─────────────────────────────────────
    // 引用関連
    // ─────────────────────────────────────
    public enum DebateDirection { LeftToRight, RightToLeft, ThirdSeat, ThirdToLeft, ThirdToRight }

    public sealed class QuoteReference
    {
        public string QuoteId             { get; set; } = Guid.NewGuid().ToString();
        public string SourceMessageId     { get; set; } = string.Empty;
        public string SourceParticipantId { get; set; } = string.Empty;
        public int    SourceTurnNumber    { get; set; }
        public int    StartIndex          { get; set; }
        public int    EndIndex            { get; set; }
        public string QuotedText          { get; set; } = string.Empty;
        public string QuoteType           { get; set; } = "Full";
    }

    public sealed class PartialQuote
    {
        public int            SourceTurnNumber { get; init; }
        public DebateDirection SourceDirection  { get; init; }
        public string          QuotedText       { get; init; } = string.Empty;
        public string          TargetSeat       { get; init; } = "Left";
        public DateTime        RegisteredAt     { get; init; } = DateTime.Now;

        public string ToPromptString()
            => $"【引用 Turn {SourceTurnNumber}】\n> {QuotedText}";
    }

    // ─────────────────────────────────────
    // LogReaderPayload（JSON受け渡し用）
    // ─────────────────────────────────────
    public sealed class LogReaderPayload
    {
        public List<TransferRecord> Records    { get; set; } = new();
        public int                  StartIndex { get; set; }
        public string               PipeId     { get; set; } = string.Empty;
    }

    // ─────────────────────────────────────
    // 部分引用コールバック（名前付きパイプ用）
    // ─────────────────────────────────────
    public sealed class QuoteCallbackPayload
    {
        public int    SourceTurnNumber { get; set; }
        public string QuotedText       { get; set; } = string.Empty;
        public string TargetSeat       { get; set; } = "Left"; // "Left" / "Right" / "Clipboard"
        public string Direction        { get; set; } = string.Empty;
    }
}
