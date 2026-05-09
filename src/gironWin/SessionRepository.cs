using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace gironWin
{
    /// <summary>
    /// 自動討論の各ターンを JSONL ファイルに追記し、Markdown / JSON エクスポートを提供する。
    /// </summary>
    public class SessionRepository
    {
        public string SessionFolder { get; } =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "gironWin", "sessions");

        private string _jsonlPath = string.Empty;
        private readonly List<TurnEntry> _entries = new();
        private readonly object _lock = new();

        private record TurnEntry(
            int    Turn,
            string Side,
            string Text,
            DateTime Timestamp);

        // セッション開始（Start ボタン昨歾に呼び出す）
        public void StartNewSession()
        {
            lock (_lock)
            {
                _entries.Clear();
                Directory.CreateDirectory(SessionFolder);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _jsonlPath = Path.Combine(SessionFolder, $"session_{stamp}.jsonl");
            }
        }

        // ターン追記
        public async Task AppendAsync(int turn, string side, string text)
        {
            var entry = new TurnEntry(turn, side, text, DateTime.Now);
            lock (_lock)
            {
                _entries.Add(entry);
            }

            if (string.IsNullOrEmpty(_jsonlPath)) return;

            string line = JsonSerializer.Serialize(new
            {
                turn,
                side,
                text,
                timestamp = entry.Timestamp.ToString("o")
            });

            await File.AppendAllTextAsync(_jsonlPath, line + "\n", Encoding.UTF8);
        }

        // Markdown エクスポート → 保存先パスを返す
        public async Task<string> ExportMarkdownAsync()
        {
            Directory.CreateDirectory(SessionFolder);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path  = Path.Combine(SessionFolder, $"export_{stamp}.md");

            var sb = new StringBuilder();
            sb.AppendLine("# 自動討論ログ");
            sb.AppendLine();
            sb.AppendLine($"> エクスポート: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            List<TurnEntry> snapshot;
            lock (_lock) { snapshot = new List<TurnEntry>(_entries); }

            if (snapshot.Count == 0)
            {
                sb.AppendLine("レコードがありません。自動討論を開始してからエクスポートしてください。");
            }
            else
            {
                foreach (var e in snapshot)
                {
                    sb.AppendLine($"## ターン {e.Turn} — {e.Side}");
                    sb.AppendLine($"_({e.Timestamp:HH:mm:ss})_");
                    sb.AppendLine();
                    sb.AppendLine(e.Text);
                    sb.AppendLine();
                    sb.AppendLine("---");
                    sb.AppendLine();
                }
            }

            await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
            return path;
        }

        // JSON エクスポート → 保存先パスを返す
        public async Task<string> ExportJsonAsync()
        {
            Directory.CreateDirectory(SessionFolder);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path  = Path.Combine(SessionFolder, $"export_{stamp}.json");

            List<TurnEntry> snapshot;
            lock (_lock) { snapshot = new List<TurnEntry>(_entries); }

            var data = snapshot.ConvertAll(e => new
            {
                e.Turn,
                e.Side,
                e.Text,
                Timestamp = e.Timestamp.ToString("o")
            });

            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json, Encoding.UTF8);
            return path;
        }
    }
}
