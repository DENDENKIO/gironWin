using System;
using System.Collections.Generic;

namespace gironWin
{
    /// <summary>
    /// 1回の転送・発言を表すレコード。
    /// 仕様書 Message モデルに準拠し、Phase 3 以降の引用・承認に備えて拡張済み。
    /// </summary>
    public class TransferRecord
    {
        // --- 基本情報 ---
        public string MessageId { get; set; } = Guid.NewGuid().ToString();
        public string SessionId { get; set; } = string.Empty;
        public int TurnNumber { get; set; } = 0;

        // --- 参加者・方向 ---
        public string SourceSite { get; set; } = string.Empty;
        public string TargetSite { get; set; } = string.Empty;
        public string Direction { get; set; } = string.Empty;

        // --- 本文 ---
        public string Text { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;

        // --- 送信制御 ---
        public bool Submitted { get; set; }
        public string ApprovalStatus { get; set; } = ApprovalStatuses.NotRequired;

        // --- 引用 (Phase 3 で使用) ---
        public List<string> QuotedMessageIds { get; set; } = new();

        // --- 配信先 (Phase 3 で使用) ---
        public List<string> DeliveryTargets { get; set; } = new();

        // --- 状態・時刻 ---
        public string Status { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public DateTime? DeliveredAt { get; set; }

        // --- 表示用プロパティ ---
        public string TimestampText => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
        public string SubmittedText => Submitted ? "送信" : "入力のみ";
        public string PreviewText => string.IsNullOrWhiteSpace(Text)
            ? string.Empty
            : (Text.Length > 120 ? Text[..120] + "…" : Text);
        public string SummaryOrPreview => string.IsNullOrWhiteSpace(Summary)
            ? PreviewText
            : Summary;
    }

    public static class ApprovalStatuses
    {
        public const string NotRequired = "承認不要";
        public const string Pending = "承認待ち";
        public const string Approved = "承認済み";
        public const string Rejected = "却下";
        public const string Cancelled = "キャンセル";
    }
}
