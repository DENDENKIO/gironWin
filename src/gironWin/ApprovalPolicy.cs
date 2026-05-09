using System.Text.Json.Serialization;

namespace gironWin
{
    /// <summary>
    /// NFR-02: 承認ポリシー定義モデル。
    /// ApprovalQueue の判定条件をルール合成で制御する。
    /// </summary>
    public sealed class ApprovalPolicy
    {
        [JsonPropertyName("approvalPolicyId")]
        public string ApprovalPolicyId { get; set; } = "default";

        /// <summary>常に承認を必須とする。</summary>
        [JsonPropertyName("requireApprovalBeforeSend")]
        public bool RequireApprovalBeforeSend { get; set; } = false;

        /// <summary>引用が含まれる場合は承認必須。</summary>
        [JsonPropertyName("requireApprovalWhenQuoted")]
        public bool RequireApprovalWhenQuoted { get; set; } = true;

        /// <summary>メッセージが長い場合は承認必須。</summary>
        [JsonPropertyName("requireApprovalForLongMessage")]
        public bool RequireApprovalForLongMessage { get; set; } = true;

        /// <summary>承認が必要とみなすメッセージ長（文字数）。</summary>
        [JsonPropertyName("longMessageThreshold")]
        public int LongMessageThreshold { get; set; } = 1800;

        /// <summary>コード・仕様・証明・数式が含まれる場合は承認必須。</summary>
        [JsonPropertyName("requireApprovalForCodeOrSpec")]
        public bool RequireApprovalForCodeOrSpec { get; set; } = true;

        /// <summary>エラー復帰後の初回送信は承認必須。</summary>
        [JsonPropertyName("requireApprovalAfterRecovery")]
        public bool RequireApprovalAfterRecovery { get; set; } = true;

        // ---------------------------------------------------------------
        // 判定ロジック
        // ---------------------------------------------------------------

        /// <summary>
        /// このポリシーに基づいて送信前承認が必要かを判定する。
        /// </summary>
        public bool ShouldRequireApproval(
            string messageText,
            bool   hasQuote        = false,
            bool   isAfterRecovery = false)
        {
            if (RequireApprovalBeforeSend)                              return true;
            if (hasQuote && RequireApprovalWhenQuoted)                  return true;
            if (isAfterRecovery && RequireApprovalAfterRecovery)        return true;
            if (RequireApprovalForLongMessage &&
                messageText.Length > LongMessageThreshold)              return true;
            if (RequireApprovalForCodeOrSpec && ContainsCodeOrSpec(messageText))
                                                                        return true;
            return false;
        }

        private static bool ContainsCodeOrSpec(string text) =>
            text.Contains("```")     ||
            text.Contains("def ")    ||
            text.Contains("public ") ||
            text.Contains("private ")||
            text.Contains("class ")  ||
            text.Contains("\u2200") || text.Contains("\u2203") ||
            text.Contains("\u2208") || text.Contains("\u2211") ||
            text.Contains("\u8a3c\u660e") || text.Contains("\u5b9a\u7fa9") ||
            text.Contains("\u4ed5\u69d8") || text.Contains("\u8a2d\u8a08");

        // ---------------------------------------------------------------
        // プリセット
        // ---------------------------------------------------------------

        public static ApprovalPolicy Default => new()
        {
            ApprovalPolicyId              = "default",
            RequireApprovalBeforeSend     = false,
            RequireApprovalWhenQuoted     = true,
            RequireApprovalForLongMessage = true,
            LongMessageThreshold          = 1800,
            RequireApprovalForCodeOrSpec  = true,
            RequireApprovalAfterRecovery  = true
        };

        public static ApprovalPolicy AlwaysAsk => new()
        {
            ApprovalPolicyId          = "always-ask",
            RequireApprovalBeforeSend = true
        };

        public static ApprovalPolicy FullAuto => new()
        {
            ApprovalPolicyId              = "full-auto",
            RequireApprovalBeforeSend     = false,
            RequireApprovalWhenQuoted     = false,
            RequireApprovalForLongMessage = false,
            RequireApprovalForCodeOrSpec  = false,
            RequireApprovalAfterRecovery  = false
        };
    }
}
