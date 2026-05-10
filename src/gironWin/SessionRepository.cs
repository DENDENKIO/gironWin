using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace gironWin
{
    /// <summary>
    /// NFR-06: セッション永続化。
    /// TransferRecord に加え ResearchTagEntry / QuoteReference も保存対象とする。
    /// </summary>
    public class SessionRepository
    {
        public string SessionFolder { get; } =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "gironWin", "sessions");

        private string _jsonlPath  = string.Empty;
        private readonly List<TurnEntry>        _entries      = new();
        private readonly List<ResearchTagEntry> _researchTags = new();
        private readonly List<QuoteReference>   _quotes       = new();
        private readonly object _lock = new();

        // セッション内の全エントリを外部から参照できるよう公開
        public IReadOnlyList<TurnEntry>        Entries      => _entries;
        public IReadOnlyList<ResearchTagEntry> ResearchTags => _researchTags;
        public IReadOnlyList<QuoteReference>   Quotes       => _quotes;

        // ---------------------------------------------------------------
        // TurnEntry（内部レコード）
        // ---------------------------------------------------------------

        public record TurnEntry(
            int      TurnNumber,
            string   Side,
            string   Direction,
            string   Text,
            string   Summary,
            string   MessageId,
            DateTime Timestamp);

        // ---------------------------------------------------------------
        // セッション開始
        // ---------------------------------------------------------------

        public void StartNewSession()
        {
            lock (_lock)
            {
                _entries.Clear();
                _researchTags.Clear();
                _quotes.Clear();
                Directory.CreateDirectory(SessionFolder);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _jsonlPath = Path.Combine(SessionFolder, $"session_{stamp}.jsonl");
            }
        }

        // ---------------------------------------------------------------
        // ターン追記（TransferRecord 版）
        // ---------------------------------------------------------------

        public async Task AppendAsync(TransferRecord record)
        {
            var entry = new TurnEntry(
                record.TurnNumber,
                record.Direction,
                record.Direction,
                record.Text    ?? string.Empty,
                record.Summary ?? string.Empty,
                record.MessageId,
                DateTime.Now);

            lock (_lock) { _entries.Add(entry); }

            if (string.IsNullOrEmpty(_jsonlPath)) return;

            string line = JsonSerializer.Serialize(new
            {
                type      = "turn",
                turn      = entry.TurnNumber,
                side      = entry.Side,
                direction = entry.Direction,
                text      = entry.Text,
                summary   = entry.Summary,
                messageId = entry.MessageId,
                timestamp = entry.Timestamp.ToString("o")
            });
            await File.AppendAllTextAsync(_jsonlPath, line + "\n", Encoding.UTF8);
        }

        /// <summary>旧 API 互換ラッパー。</summary>
        public async Task AppendAsync(int turn, string side, string text)
        {
            var dummy = new TransferRecord
            {
                TurnNumber = turn,
                Direction  = side,
                Text       = text,
                Summary    = string.Empty,
                MessageId  = $"msg-{turn}-{side}"
            };
            await AppendAsync(dummy);
        }

        // ---------------------------------------------------------------
        // 研究タグ追記
        // ---------------------------------------------------------------

        public async Task AppendResearchTagAsync(ResearchTagEntry tag)
        {
            lock (_lock) { _researchTags.Add(tag); }

            if (string.IsNullOrEmpty(_jsonlPath)) return;

            string line = JsonSerializer.Serialize(new
            {
                type       = "researchTag",
                tagType    = tag.TagType,
                subTagType = tag.SubTagType,
                text       = tag.Text,
                turnNumber = tag.TurnNumber,
                messageId  = tag.MessageId,
                importance = tag.Importance
            });
            await File.AppendAllTextAsync(_jsonlPath, line + "\n", Encoding.UTF8);
        }

        // ---------------------------------------------------------------
        // 引用追記
        // ---------------------------------------------------------------

        public async Task AppendQuoteAsync(QuoteReference quote)
        {
            lock (_lock) { _quotes.Add(quote); }

            if (string.IsNullOrEmpty(_jsonlPath)) return;

            string line = JsonSerializer.Serialize(new
            {
                type                = "quote",
                quoteId             = quote.QuoteId,
                sourceMessageId     = quote.SourceMessageId,
                sourceParticipantId = quote.SourceParticipantId,
                sourceTurnNumber    = quote.SourceTurnNumber,
                quotedText          = quote.QuotedText,
                quoteType           = quote.QuoteType
            });
            await File.AppendAllTextAsync(_jsonlPath, line + "\n", Encoding.UTF8);
        }

        // ---------------------------------------------------------------
        // TransferRecord リストに変換（ExportService に渡す用）
        // ---------------------------------------------------------------

        public List<TransferRecord> ToTransferRecords()
        {
            lock (_lock)
            {
                return _entries.ConvertAll(e => new TransferRecord
                {
                    TurnNumber = e.TurnNumber,
                    Direction  = e.Direction,
                    Text       = e.Text,
                    Summary    = e.Summary,
                    MessageId  = e.MessageId
                });
            }
        }


    }
}
