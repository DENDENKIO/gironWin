using System;

namespace gironWin
{
    /// <summary>
    /// 履歴から引用文を生成する。
    /// FR-10 引用返信
    /// </summary>
    public static class QuoteService
    {
        /// <summary>
        /// 全文引用テキストを生成する。
        /// </summary>
        public static string BuildFullQuote(TransferRecord record)
        {
            string header = $"[Turn {record.TimestampText} | {record.Direction}]";
            string body   = Indent(record.Text ?? string.Empty);
            return $"{header}\n{body}\n\n";
        }

        /// <summary>
        /// 部分引用テキストを生成する。
        /// </summary>
        public static string BuildPartialQuote(TransferRecord record, string selectedText)
        {
            if (string.IsNullOrWhiteSpace(selectedText))
                return BuildFullQuote(record);

            string header = $"[Turn {record.TimestampText} | {record.Direction} — 部分引用]";
            string body   = Indent(selectedText.Trim());
            return $"{header}\n{body}\n\n";
        }

        private static string Indent(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "> (empty)";
            var lines = text.Split('\n');
            return "> " + string.Join("\n> ", lines);
        }
    }
}
