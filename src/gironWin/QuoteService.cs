using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace gironWin
{
    /// <summary>
    /// FR-10: 全文・部分引用の管理、引用を含む送信文の組み立て。
    /// </summary>
    public class QuoteService
    {
        // 引用レジストリ（セッション内全引用を保持）
        public ObservableCollection<QuoteReference> References { get; } = new();

        // ---------------------------------------------------------------
        // 引用登録
        // ---------------------------------------------------------------

        /// <summary>全文引用を登録して QuoteReference を返す。</summary>
        public QuoteReference AddFullQuote(TransferRecord record, string participantId)
        {
            var q = new QuoteReference
            {
                SourceMessageId     = record.MessageId,
                SourceParticipantId = participantId,
                SourceTurnNumber    = record.TurnNumber,
                StartIndex          = 0,
                EndIndex            = (record.Text ?? string.Empty).Length,
                QuotedText          = record.Text ?? string.Empty,
                QuoteType           = "Full"
            };
            References.Add(q);
            return q;
        }

        /// <summary>部分引用を登録して QuoteReference を返す。</summary>
        public QuoteReference AddPartialQuote(
            TransferRecord record, string participantId,
            string selectedText, int startIndex = 0)
        {
            if (string.IsNullOrWhiteSpace(selectedText))
                return AddFullQuote(record, participantId);

            int end = startIndex + selectedText.Length;
            var q = new QuoteReference
            {
                SourceMessageId     = record.MessageId,
                SourceParticipantId = participantId,
                SourceTurnNumber    = record.TurnNumber,
                StartIndex          = startIndex,
                EndIndex            = end,
                QuotedText          = selectedText.Trim(),
                QuoteType           = "Partial"
            };
            References.Add(q);
            return q;
        }

        // ---------------------------------------------------------------
        // 送信文への埋め込み（プレビュー用）
        // ---------------------------------------------------------------

        /// <summary>
        /// 引用ブロック + 返信本文を合成した送信プレビューを返す。
        /// </summary>
        public string BuildQuotedMessage(
            IEnumerable<QuoteReference> quotes, string replyBody)
        {
            var sb = new System.Text.StringBuilder();

            foreach (var q in quotes)
            {
                string typeLabel = q.QuoteType == "Full" ? "\u5168\u6587\u5f15\u7528" : "\u90e8\u5206\u5f15\u7528";
                sb.AppendLine(
                    $"[Turn {q.SourceTurnNumber} | {q.SourceParticipantId} \u2014 {typeLabel}]");
                foreach (var line in q.QuotedText.Split('\n'))
                    sb.AppendLine($"> {line}");
                sb.AppendLine();
            }

            sb.AppendLine(replyBody);
            return sb.ToString().TrimEnd();
        }

        /// <summary>旧 static API との互換ラッパー（全文）。</summary>
        public static string BuildFullQuote(TransferRecord record)
        {
            string header = $"[Turn {record.TurnNumber} | {record.Direction}]";
            string body   = Indent(record.Text ?? string.Empty);
            return $"{header}\n{body}\n\n";
        }

        /// <summary>旧 static API との互換ラッパー（部分）。</summary>
        public static string BuildPartialQuote(
            TransferRecord record, string selectedText)
        {
            if (string.IsNullOrWhiteSpace(selectedText))
                return BuildFullQuote(record);

            string header =
                $"[Turn {record.TurnNumber} | {record.Direction} \u2014 \u90e8\u5206\u5f15\u7528]";
            string body = Indent(selectedText.Trim());
            return $"{header}\n{body}\n\n";
        }

        // ---------------------------------------------------------------
        // クエリ
        // ---------------------------------------------------------------

        public IReadOnlyList<QuoteReference> GetByMessage(string messageId) =>
            References.Where(q => q.SourceMessageId == messageId).ToList();

        public void Clear() => References.Clear();

        private readonly List<PartialQuote> _partialQuotes = new();
        public IReadOnlyList<PartialQuote> PartialQuotes => _partialQuotes;
        public void AddPartialQuote(PartialQuote q) => _partialQuotes.Add(q);
        public void ClearPartialQuotes() => _partialQuotes.Clear();

        // ---------------------------------------------------------------
        private static string Indent(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "> (empty)";
            return "> " + string.Join("\n> ", text.Split('\n'));
        }
    }

}
