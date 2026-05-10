using System;
using System.Collections.Generic;
using System.Threading;

namespace gironWin
{
    // ---------------------------------------------------------------
    // ログレベル
    // ---------------------------------------------------------------
    public enum LogLevel
    {
        Debug,   // 詳細な内部動作（通常は非表示）
        Info,    // 通常動作の記録
        Warn,    // 問題になりうる状態
        Error    // エラー・例外
    }

    // ---------------------------------------------------------------
    // ログカテゴリ定数（呼び出し元で文字列ハードコードしない）
    // ---------------------------------------------------------------
    public static class LogCategory
    {
        public const string RunLoop   = "RunLoop";    // 討論ループ全体
        public const string Turn      = "Turn";       // ターン制御
        public const string Monitor   = "Monitor";    // ConversationMonitor
        public const string Transfer  = "Transfer";   // TransferService
        public const string Adapter   = "Adapter";    // AI サイトアダプタ
        public const string Approval  = "Approval";   // 承認キュー
        public const string Session   = "Session";    // セッション・ログ保存
        public const string Research  = "Research";   // 研究タグ抽出
        public const string System    = "System";     // アプリ全般
    }

    // ---------------------------------------------------------------
    // ログエントリ（1行分）
    // ---------------------------------------------------------------
    public sealed class AppLogEntry
    {
        public DateTime  Timestamp { get; }  = DateTime.Now;
        public LogLevel  Level     { get; }
        public string    Category  { get; }
        public string    Message   { get; }
        public string    ThreadId  { get; }

        /// <summary>UI 表示用フォーマット済み文字列</summary>
        public string FormattedLine =>
            $"[{Timestamp:HH:mm:ss.fff}][{Level,-5}][{Category,-8}] {Message}";

        public AppLogEntry(LogLevel level, string category, string message)
        {
            Level    = level;
            Category = category;
            Message  = message;
            ThreadId = Thread.CurrentThread.ManagedThreadId.ToString();
        }
    }

    // ---------------------------------------------------------------
    // AppLogger — 静的グローバル API
    // どこからでも AppLogger.Log(...) で送信できる
    // ---------------------------------------------------------------
    public static class AppLogger
    {
        /// <summary>
        /// 新しいログエントリが追加されたときに発火。
        /// DebugLogWindow が購読してリアルタイム表示する。
        /// </summary>
        public static event EventHandler<AppLogEntry>? EntryAdded;

        // 直近 MaxBuffer 件を保持（DebugLogWindow が開く前のログも表示できるよう）
        private const int MaxBuffer = 5000;
        private static readonly List<AppLogEntry> _buffer = new();
        private static readonly object _lock = new();

        // ---------------------------------------------------------------
        // 公開 API
        // ---------------------------------------------------------------

        public static void Debug(string category, string message)
            => Append(LogLevel.Debug, category, message);

        public static void Info(string category, string message)
            => Append(LogLevel.Info, category, message);

        public static void Warn(string category, string message)
            => Append(LogLevel.Warn, category, message);

        public static void Error(string category, string message)
            => Append(LogLevel.Error, category, message);

        /// <summary>汎用オーバーロード（レベルを明示する場合）</summary>
        public static void Log(LogLevel level, string category, string message)
            => Append(level, category, message);

        /// <summary>例外付きエラーログ</summary>
        public static void Error(string category, string message, Exception ex)
            => Append(LogLevel.Error, category, $"{message} | {ex.GetType().Name}: {ex.Message}");

        /// <summary>起動時など過去ログを一括取得（DebugLogWindow の初期化用）</summary>
        public static IReadOnlyList<AppLogEntry> GetBuffer()
        {
            lock (_lock) return _buffer.ToArray();
        }

        public static void Clear()
        {
            lock (_lock) _buffer.Clear();
        }

        // ---------------------------------------------------------------
        // 内部
        // ---------------------------------------------------------------

        private static void Append(LogLevel level, string category, string message)
        {
            var entry = new AppLogEntry(level, category, message);
            lock (_lock)
            {
                if (_buffer.Count >= MaxBuffer)
                    _buffer.RemoveAt(0);
                _buffer.Add(entry);
            }
            // イベントはロック外で発火（デッドロック防止）
            EntryAdded?.Invoke(null, entry);
        }
    }
}
