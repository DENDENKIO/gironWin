using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace gironWin
{
    /// <summary>
    /// セッションのログ保存・読み込み・エクスポートを担当。
    /// 仕様書 FR-04「自動取得と保存」/ FR-14「成果物生成」対応。
    /// 外部ライブラリなし。保存先: %LOCALAPPDATA%\gironWin\sessions\
    /// </summary>
    public sealed class SessionRepository
    {
        // ── パス ──────────────────────────────────────────
        private static readonly string BaseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "gironWin", "sessions");

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        // ──────────────────────────────────────────────────

        private string _sessionId = string.Empty;
        private readonly List<TransferRecord> _records = new();

        public string SessionId => _sessionId;

        /// <summary>新しいセッションを開始する。</summary>
        public void StartSession(string? sessionId = null)
        {
            _sessionId = sessionId ?? $"sess-{DateTime.Now:yyyyMMdd-HHmmss}";
            _records.Clear();
            Directory.CreateDirectory(BaseDir);
        }

        /// <summary>レコードを追加し、即座にファイルへ追記保存する。</summary>
        public async Task AppendAsync(TransferRecord record)
        {
            if (string.IsNullOrEmpty(_sessionId)) StartSession();

            record.SessionId = _sessionId;
            _records.Add(record);

            // JSON Lines 形式で追記（1行1レコード → 軽量）
            string jsonlPath = Path.Combine(BaseDir, $"{_sessionId}.jsonl");
            string line = JsonSerializer.Serialize(record, _jsonOptions
                .GetType() == typeof(JsonSerializerOptions) ? _jsonOptions : null)
                .Replace("\r", "").Replace("\n", " ");

            await File.AppendAllTextAsync(jsonlPath, line + "\n", Encoding.UTF8);
        }

        // ── エクスポート ──────────────────────────────────

        /// <summary>現セッションを Markdown ファイルに書き出し、パスを返す。</summary>
        public async Task<string> ExportMarkdownAsync()
        {
            if (string.IsNullOrEmpty(_sessionId)) throw new InvalidOperationException("セッションが開始されていません。");

            string path = Path.Combine(BaseDir, $"{_sessionId}.md");
            var sb = new StringBuilder();
            sb.AppendLine($"# 討論ログ — {_sessionId}");
            sb.AppendLine();
            sb.AppendLine($"生成日時: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"総ターン数: {_records.Count}");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            foreach (var r in _records)
            {
                sb.AppendLine($"## ターン {r.TurnNumber} — {r.Direction}");
                sb.AppendLine($"_日時: {r.TimestampText}  |  承認: {r.ApprovalStatus}  |  送信: {r.SubmittedText}_");
                sb.AppendLine();
                sb.AppendLine(r.Text);
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }

            await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
            return path;
        }

        /// <summary>現セッションを JSON ファイルに書き出し、パスを返す。</summary>
        public async Task<string> ExportJsonAsync()
        {
            if (string.IsNullOrEmpty(_sessionId)) throw new InvalidOperationException("セッションが開始されていません。");

            string path = Path.Combine(BaseDir, $"{_sessionId}.json");
            var payload = new
            {
                sessionId  = _sessionId,
                exportedAt = DateTime.Now,
                turnCount  = _records.Count,
                records    = _records
            };
            string json = JsonSerializer.Serialize(payload, _jsonOptions);
            await File.WriteAllTextAsync(path, json, Encoding.UTF8);
            return path;
        }

        /// <summary>過去セッションの JSONL ファイル一覧を返す（新しい順）。</summary>
        public static IEnumerable<string> ListSessionFiles()
        {
            if (!Directory.Exists(BaseDir)) yield break;
            var files = Directory.GetFiles(BaseDir, "*.jsonl");
            Array.Sort(files, (a, b) => string.Compare(b, a, StringComparison.Ordinal));
            foreach (var f in files) yield return f;
        }
    }
}
