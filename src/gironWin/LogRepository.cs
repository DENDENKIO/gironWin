using System;
using System.Collections.Generic;

namespace gironWin
{
    /// <summary>
    /// 討論メッセージのログを保持するリポジトリ。
    /// AutoDebateService から各ターンの発言を記録する。
    /// </summary>
    public sealed class LogRepository
    {
        private readonly List<MessageLogEntry> _entries = new();
        private readonly object _lock = new();
        private string _sessionId = string.Empty;
        private int _sequence;

        public IReadOnlyList<MessageLogEntry> Entries
        {
            get { lock (_lock) { return _entries.ToArray(); } }
        }

        public string CurrentSessionId => _sessionId;

        /// <summary>
        /// 新しいセッションを開始し、内部状態をリセットする。
        /// </summary>
        public void StartSession()
        {
            lock (_lock)
            {
                _entries.Clear();
                _sequence  = 0;
                _sessionId = $"S{DateTime.Now:yyyyMMdd_HHmmss}";
            }
        }

        /// <summary>
        /// メッセージをログに追加する。
        /// </summary>
        public MessageLogEntry AddEntry(
            string participantId,
            string siteName,
            string role,
            int    turnNumber,
            string rawText)
        {
            lock (_lock)
            {
                _sequence++;
                if (string.IsNullOrEmpty(_sessionId))
                    _sessionId = $"S{DateTime.Now:yyyyMMdd_HHmmss}";

                var entry = new MessageLogEntry
                {
                    MessageId     = $"M{_sequence:D5}",
                    SessionId     = _sessionId,
                    ParticipantId = participantId,
                    SiteName      = siteName,
                    Role          = role,
                    TurnNumber    = turnNumber,
                    RawText       = rawText,
                    Timestamp     = DateTime.Now
                };

                _entries.Add(entry);
                return entry;
            }
        }
    }

    /// <summary>
    /// 1 件のメッセージログエントリ。
    /// </summary>
    public sealed class MessageLogEntry
    {
        public string   MessageId     { get; init; } = string.Empty;
        public string   SessionId     { get; init; } = string.Empty;
        public string   ParticipantId { get; init; } = string.Empty;
        public string   SiteName      { get; init; } = string.Empty;
        public string   Role          { get; init; } = string.Empty;
        public int      TurnNumber    { get; init; }
        public string   RawText       { get; init; } = string.Empty;
        public DateTime Timestamp     { get; init; }
    }
}
