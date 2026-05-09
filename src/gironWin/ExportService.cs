using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace gironWin
{
    /// <summary>
    /// FR-14: 議論終了後の成果物生成。
    /// 合意点・対立点・引用根拠・次アクション・仕様案・研究ノートを出力する。
    /// Markdown / JSON / txt の3形式に対応。
    /// </summary>
    public sealed class ExportService
    {
        public string ExportFolder { get; } =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "gironWin", "exports");

        // ---------------------------------------------------------------
        // Markdown エクスポート（メイン成果物）
        // ---------------------------------------------------------------

        public async Task<string> ExportMarkdownAsync(
            IReadOnlyList<TransferRecord>   records,
            IReadOnlyList<QuoteReference>   quotes,
            IReadOnlyList<ResearchTagEntry> researchTags,
            DebatePreset?  preset = null,
            string?        topic  = null)
        {
            Directory.CreateDirectory(ExportFolder);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path  = Path.Combine(ExportFolder, $"debate_result_{stamp}.md");

            var sb = new StringBuilder();

            // ヘッダ
            sb.AppendLine("# AI\u8a0e\u8ad6\u30ef\u30fc\u30af\u30d9\u30f3\u30c1 \u2014 \u6210\u679c\u7269\u30ec\u30dd\u30fc\u30c8");
            sb.AppendLine();
            sb.AppendLine($"> \u751f\u6210\u65e5\u6642: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            if (!string.IsNullOrWhiteSpace(topic))
                sb.AppendLine($"> \u8b70\u984c: {topic}");
            if (preset != null)
            {
                sb.AppendLine($"> \u30d7\u30ea\u30bb\u30c3\u30c8: {preset.Name}");
                sb.AppendLine($"> TurnPolicy: {preset.TurnPolicy}");
                if (preset.ResearchMode)
                    sb.AppendLine("> \u30e2\u30fc\u30c9: \ud83d\udd2c \u6570\u5b66\u7814\u7a76\u30e2\u30fc\u30c9");
            }
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            // 合意点・対立点
            AppendAgreements(sb, records);

            // 引用根拠
            if (quotes.Count > 0)
            {
                sb.AppendLine("## \u5f15\u7528\u6839\u62e0");
                sb.AppendLine();
                foreach (var q in quotes)
                {
                    string typeLabel = q.QuoteType == "Full" ? "\u5168\u6587" : "\u90e8\u5206";
                    sb.AppendLine(
                        $"- **Turn {q.SourceTurnNumber}** " +
                        $"[{q.SourceParticipantId} / {typeLabel}\u5f15\u7528]");
                    sb.AppendLine($"  > {q.QuotedText.Replace("\n", "\n  > ")}");
                }
                sb.AppendLine();
            }

            // 研究ノート
            if (researchTags.Count > 0)
            {
                sb.AppendLine("## \u7814\u7a76\u30ce\u30fc\u30c8");
                sb.AppendLine();
                foreach (var grp in researchTags.GroupBy(t => t.TagType))
                {
                    sb.AppendLine($"### {grp.Key}");
                    foreach (var t in grp)
                        sb.AppendLine($"- Turn {t.TurnNumber}: {t.Content}");
                    sb.AppendLine();
                }
            }

            // 発言ログ全文
            sb.AppendLine("## \u767a\u8a00\u30ed\u30b0");
            sb.AppendLine();
            foreach (var r in records)
            {
                sb.AppendLine($"### Turn {r.TurnNumber} \u2014 {r.Direction}");
                sb.AppendLine($"_{r.TimestampText}_");
                sb.AppendLine();
                sb.AppendLine(r.Text);
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }

            // 次アクション欄
            sb.AppendLine("## \u6b21\u30a2\u30af\u30b7\u30e7\u30f3");
            sb.AppendLine();
            sb.AppendLine("- [ ] \uff08\u3053\u3053\u306b\u30e6\u30fc\u30b6\u30fc\u304c\u8a18\u5165\uff09");
            sb.AppendLine();

            await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
            return path;
        }

        // ---------------------------------------------------------------
        // JSON エクスポート
        // ---------------------------------------------------------------

        public async Task<string> ExportJsonAsync(
            IReadOnlyList<TransferRecord>   records,
            IReadOnlyList<QuoteReference>   quotes,
            IReadOnlyList<ResearchTagEntry> researchTags,
            DebatePreset? preset = null,
            string?       topic  = null)
        {
            Directory.CreateDirectory(ExportFolder);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path  = Path.Combine(ExportFolder, $"debate_result_{stamp}.json");

            var payload = new
            {
                exportedAt   = DateTime.Now.ToString("o"),
                topic        = topic ?? string.Empty,
                preset       = preset?.Name ?? string.Empty,
                turnPolicy   = preset?.TurnPolicy.ToString() ?? string.Empty,
                researchMode = preset?.ResearchMode ?? false,
                records      = records.Select(r => new
                {
                    r.TurnNumber,
                    r.Direction,
                    r.Text,
                    r.Summary,
                    r.TimestampText
                }),
                quotes = quotes.Select(q => new
                {
                    q.QuoteId,
                    q.SourceMessageId,
                    q.SourceParticipantId,
                    q.SourceTurnNumber,
                    q.QuotedText,
                    q.QuoteType
                }),
                researchTags = researchTags.Select(t => new
                {
                    t.TagType,
                    t.TurnNumber,
                    t.Content,
                    t.MessageId
                })
            };

            string json = JsonSerializer.Serialize(
                payload,
                new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json, Encoding.UTF8);
            return path;
        }

        // ---------------------------------------------------------------
        // プレーンテキスト エクスポート
        // ---------------------------------------------------------------

        public async Task<string> ExportTxtAsync(
            IReadOnlyList<TransferRecord> records,
            string? topic = null)
        {
            Directory.CreateDirectory(ExportFolder);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path  = Path.Combine(ExportFolder, $"debate_log_{stamp}.txt");

            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(topic))
                sb.AppendLine($"\u8b70\u984c: {topic}");
            sb.AppendLine($"\u30a8\u30af\u30b9\u30dd\u30fc\u30c8: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine(new string('=', 60));
            sb.AppendLine();

            foreach (var r in records)
            {
                sb.AppendLine(
                    $"[Turn {r.TurnNumber}] {r.Direction}  {r.TimestampText}");
                sb.AppendLine(r.Text);
                sb.AppendLine(new string('-', 40));
                sb.AppendLine();
            }

            await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
            return path;
        }

        // ---------------------------------------------------------------
        // ヘルパー
        // ---------------------------------------------------------------

        private static void AppendAgreements(
            StringBuilder sb, IReadOnlyList<TransferRecord> records)
        {
            var agreed  = records.Where(r => ContainsAgreement(r.Text)).ToList();
            var opposed = records.Where(r => ContainsOpposition(r.Text)).ToList();

            sb.AppendLine("## \u5408\u610f\u70b9");
            sb.AppendLine();
            if (agreed.Count > 0)
                foreach (var r in agreed)
                    sb.AppendLine($"- Turn {r.TurnNumber} [{r.Direction}]: {r.Summary}");
            else
                sb.AppendLine("\uff08\u660e\u78ba\u306a\u5408\u610f\u70b9\u306f\u691c\u51fa\u3055\u308c\u307e\u305b\u3093\u3067\u3057\u305f\uff09");
            sb.AppendLine();

            sb.AppendLine("## \u5bfe\u7acb\u70b9");
            sb.AppendLine();
            if (opposed.Count > 0)
                foreach (var r in opposed)
                    sb.AppendLine($"- Turn {r.TurnNumber} [{r.Direction}]: {r.Summary}");
            else
                sb.AppendLine("\uff08\u660e\u78ba\u306a\u5bfe\u7acb\u70b9\u306f\u691c\u51fa\u3055\u308c\u307e\u305b\u3093\u3067\u3057\u305f\uff09");
            sb.AppendLine();

            sb.AppendLine("## \u672a\u89e3\u6c7a\u70b9");
            sb.AppendLine();
            sb.AppendLine("\uff08\u30e6\u30fc\u30b6\u30fc\u307e\u305f\u306f\u53f8\u4f1a\u304c\u8a18\u5165\uff09");
            sb.AppendLine();
        }

        private static bool ContainsAgreement(string text) =>
            text.Contains("\u540c\u610f") || text.Contains("\u8cdb\u6210") ||
            text.Contains("\u305d\u306e\u901a\u308a") || text.Contains("\u540c\u69d8") ||
            text.Contains("agree",  StringComparison.OrdinalIgnoreCase) ||
            text.Contains("correct",StringComparison.OrdinalIgnoreCase);

        private static bool ContainsOpposition(string text) =>
            text.Contains("\u53cd\u8ad6") || text.Contains("\u3057\u304b\u3057") ||
            text.Contains("\u4e00\u65b9") || text.Contains("\u554f\u984c") ||
            text.Contains("disagree",StringComparison.OrdinalIgnoreCase) ||
            text.Contains("however", StringComparison.OrdinalIgnoreCase);
    }
}
