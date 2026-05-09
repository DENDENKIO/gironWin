using System;

namespace gironWin
{
    public class TransferRecord
    {
        public DateTime Timestamp { get; set; }
        public string SourceSite { get; set; } = "";
        public string TargetSite { get; set; } = "";
        public string Direction { get; set; } = "";
        public string Text { get; set; } = "";
        public bool Submitted { get; set; }
        public string Status { get; set; } = "";

        public string TimestampText => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
        public string SubmittedText => Submitted ? "送信" : "入力のみ";
        public string PreviewText => string.IsNullOrWhiteSpace(Text)
            ? ""
            : (Text.Length > 120 ? Text.Substring(0, 120) + "..." : Text);
    }
}
