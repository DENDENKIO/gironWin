using System.Collections.Generic;
using System.ComponentModel;

namespace gironWin
{
    public static class ApprovalStatuses
    {
        public const string Pending  = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
    }

    /// <summary>
    /// 1ターン分の転送記録。Phase 3-5 拡張: Summary・QuotedMessageIds・ResearchTags 追加。
    /// </summary>
    public sealed class TransferRecord : INotifyPropertyChanged
    {
        private string _approvalStatus = ApprovalStatuses.Pending;

        public int    TurnNumber { get; set; }
        public string Direction  { get; set; } = string.Empty;
        public string Text       { get; set; } = string.Empty;

        public System.DateTime Timestamp       { get; set; } = System.DateTime.Now;
        public string TimestampText             => Timestamp.ToString("HH:mm:ss");
        public string SourceSite               { get; set; } = string.Empty;
        public string TargetSite               { get; set; } = string.Empty;
        public bool   Submitted                { get; set; }
        public string Status                   { get; set; } = string.Empty;
        public string ParticipantRole          { get; set; } = string.Empty;
        public string SessionId                { get; set; } = string.Empty;
        public string MessageId                { get; set; } = System.Guid.NewGuid().ToString();

        /// <summary>FR-11: 1行要約（Phase 3 SummaryService が自動生成）</summary>
        public string Summary    { get; set; } = string.Empty;

        /// <summary>FR-11: このレコードに対応するログエントリ ID</summary>
        public string MessageLogEntryId { get; set; } = string.Empty;

        /// <summary>FR-11: 引用元 messageId 一覧（Phase 3 QuoteService が設定）</summary>
        public List<string> QuotedMessageIds { get; set; } = new();

        /// <summary>FR-11: 送信先サイト名</summary>
        public string DeliveryTarget { get; set; } = string.Empty;

        /// <summary>FR-13: Phase 5 研究タグ一覧</summary>
        public List<ResearchTagEntry> ResearchTags { get; set; } = new();

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
            _                        => "⏳"
        };

        public List<ParagraphBlock> ParagraphBlocks { get; set; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public sealed class ParagraphBlock
    {
        public int    Index     { get; set; }
        public int    CharStart { get; set; }
        public int    CharEnd   { get; set; }
        public string Text      { get; set; } = string.Empty;
    }
}
