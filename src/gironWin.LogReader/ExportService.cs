// src/gironWin.LogReader/ExportService.cs
using gironWin.Shared;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace gironWin.LogReader
{
    public enum LogExportFormat { Html, Markdown, Text }
    public enum LogExportMode   { Combined, Separate }

    public sealed class LogExportOptions
    {
        public LogExportFormat Format              { get; set; } = LogExportFormat.Markdown;
        public LogExportMode   Mode                { get; set; } = LogExportMode.Combined;
        public bool            IncludeMetadata     { get; set; } = true;
        public bool            PreferHtmlSnapshot  { get; set; } = true;
        public string          BaseFileName        { get; set; } = $"giron-export-{DateTime.Now:yyyyMMdd-HHmmss}";
    }

    public sealed class LogExportResult
    {
        public bool         Success     { get; init; }
        public string       Message     { get; init; } = string.Empty;
        public List<string> OutputPaths { get; init; } = new();
    }

    public sealed class ExportService
    {
        public LogExportResult Export(IReadOnlyList<TransferRecord> records, LogExportOptions options)
        {
            if (records == null || records.Count == 0)
                return Fail("エクスポート対象のターンがありません。");

            var ordered = records
                .OrderBy(x => x.TurnNumber)
                .ThenBy(x => x.Timestamp)
                .ToList();

            return options.Mode switch
            {
                LogExportMode.Combined => ExportCombined(ordered, options),
                LogExportMode.Separate => ExportSeparate(ordered, options),
                _                      => Fail("不明な出力モードです。")
            };
        }

        // ─── Combined ───────────────────────────────────────────
        private LogExportResult ExportCombined(List<TransferRecord> records, LogExportOptions options)
        {
            var dialog = new SaveFileDialog
            {
                FileName        = options.BaseFileName,
                DefaultExt      = Ext(options.Format),
                Filter          = Filter(options.Format),
                AddExtension    = true,
                OverwritePrompt = true,
                Title           = "連結エクスポートの保存先"
            };
            if (dialog.ShowDialog() != true)
                return Fail("保存がキャンセルされました。");

            string content = options.Format switch
            {
                LogExportFormat.Html     => CombinedHtml(records, options),
                LogExportFormat.Markdown => CombinedMarkdown(records, options),
                _                        => CombinedText(records, options)
            };

            File.WriteAllText(dialog.FileName, content, new UTF8Encoding(true));
            return Ok("連結エクスポートが完了しました。", dialog.FileName);
        }

        // ─── Separate ───────────────────────────────────────────
        private LogExportResult ExportSeparate(List<TransferRecord> records, LogExportOptions options)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "個別ファイルの出力先フォルダを選択してください"
            };
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FolderName))
                return Fail("出力先の選択がキャンセルされました。");

            string root = Path.Combine(dialog.FolderName, options.BaseFileName);
            Directory.CreateDirectory(root);

            var paths = new List<string>();
            foreach (var r in records)
            {
                string path = Path.Combine(root, PerTurnFileName(r, Ext(options.Format)));
                string content = options.Format switch
                {
                    LogExportFormat.Html     => PerTurnHtml(r, options),
                    LogExportFormat.Markdown => PerTurnMarkdown(r, options),
                    _                        => PerTurnText(r, options)
                };
                File.WriteAllText(path, content, new UTF8Encoding(true));
                paths.Add(path);
            }

            return new LogExportResult
            {
                Success     = true,
                Message     = $"個別エクスポートが完了しました。({paths.Count} ファイル)",
                OutputPaths = paths
            };
        }

        // ─── HTML ───────────────────────────────────────────────
        private static string CombinedHtml(List<TransferRecord> records, LogExportOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!doctype html><html lang=\"ja\"><head><meta charset=\"utf-8\">")
              .AppendLine("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
              .AppendLine("<title>ログエクスポート</title><style>")
              .AppendLine("body{font-family:'Yu Gothic UI','Meiryo',sans-serif;line-height:1.7;margin:24px;background:#f7f7f7;color:#222;}")
              .AppendLine("h1{margin-bottom:24px;}")
              .AppendLine("section{background:#fff;border:1px solid #ddd;border-radius:10px;padding:20px;margin-bottom:24px;}")
              .AppendLine(".meta{font-size:12px;color:#666;margin-bottom:14px;}")
              .AppendLine(".html-wrap{border:1px solid #ccc;border-radius:6px;padding:12px;overflow:auto;}")
              .AppendLine("pre{white-space:pre-wrap;word-break:break-word;font-family:'Consolas','Courier New',monospace;font-size:13px;}")
              .AppendLine("</style></head><body>")
              .AppendLine("<h1>AIサイトHTMLタブ 全ターンエクスポート</h1>");

            foreach (var r in records)
            {
                sb.Append("<section>")
                  .Append($"<h2>Turn {r.TurnNumber}: {He(r.SourceSite)} → {He(r.TargetSite)}</h2>");

                if (options.IncludeMetadata)
                    sb.Append("<div class=\"meta\">")
                      .Append($"日時: {He(r.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"))} ／ ")
                      .Append($"方向: {He(r.Direction)} ／ ")
                      .Append($"要約: {He(r.Summary)}")
                      .Append("</div>");

                string? snap = ReadSnapshot(r.HtmlSnapshotPath, options);
                if (snap != null)
                    sb.Append("<div class=\"html-wrap\">").Append(snap).Append("</div>");
                else
                    sb.Append("<pre>").Append(He(NormText(r.Text))).Append("</pre>");

                sb.AppendLine("</section>");
            }

            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static string PerTurnHtml(TransferRecord r, LogExportOptions options)
        {
            string? snap = ReadSnapshot(r.HtmlSnapshotPath, options);
            if (snap != null) return snap;

            return $"<!doctype html><html lang=\"ja\"><head><meta charset=\"utf-8\"><title>Turn {r.TurnNumber}</title></head>"
                 + $"<body><h1>Turn {r.TurnNumber}</h1><p>HTMLスナップショットなし</p><pre>{He(NormText(r.Text))}</pre></body></html>";
        }

        // ─── Markdown ───────────────────────────────────────────
        private static string CombinedMarkdown(List<TransferRecord> records, LogExportOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# AIサイトHTMLタブ エクスポート").AppendLine();
            foreach (var r in records)
            {
                sb.AppendLine($"## Turn {r.TurnNumber}: {r.SourceSite} → {r.TargetSite}").AppendLine();
                if (options.IncludeMetadata)
                    sb.AppendLine($"- 日時: {r.Timestamp:yyyy-MM-dd HH:mm:ss}")
                      .AppendLine($"- 方向: {r.Direction}")
                      .AppendLine($"- 要約: {r.Summary}")
                      .AppendLine();
                sb.AppendLine(ToMarkdown(r, options)).AppendLine().AppendLine("---").AppendLine();
            }
            return sb.ToString();
        }

        private static string PerTurnMarkdown(TransferRecord r, LogExportOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Turn {r.TurnNumber}: {r.SourceSite} → {r.TargetSite}").AppendLine();
            if (options.IncludeMetadata)
                sb.AppendLine($"- 日時: {r.Timestamp:yyyy-MM-dd HH:mm:ss}")
                  .AppendLine($"- 方向: {r.Direction}")
                  .AppendLine($"- 要約: {r.Summary}")
                  .AppendLine();
            sb.AppendLine(ToMarkdown(r, options));
            return sb.ToString();
        }

        // ─── Text ───────────────────────────────────────────────
        private static string CombinedText(List<TransferRecord> records, LogExportOptions options)
        {
            var sb = new StringBuilder();
            foreach (var r in records)
            {
                sb.AppendLine($"===== Turn {r.TurnNumber}: {r.SourceSite} → {r.TargetSite} =====");
                if (options.IncludeMetadata)
                    sb.AppendLine($"日時: {r.Timestamp:yyyy-MM-dd HH:mm:ss}")
                      .AppendLine($"方向: {r.Direction}")
                      .AppendLine($"要約: {r.Summary}")
                      .AppendLine();
                sb.AppendLine(ToText(r, options)).AppendLine();
            }
            return sb.ToString();
        }

        private static string PerTurnText(TransferRecord r, LogExportOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Turn {r.TurnNumber}: {r.SourceSite} → {r.TargetSite}");
            if (options.IncludeMetadata)
                sb.AppendLine($"日時: {r.Timestamp:yyyy-MM-dd HH:mm:ss}")
                  .AppendLine($"方向: {r.Direction}")
                  .AppendLine($"要約: {r.Summary}")
                  .AppendLine();
            sb.AppendLine(ToText(r, options));
            return sb.ToString();
        }

        // ─── 変換ヘルパー ────────────────────────────────────────
        private static string ToMarkdown(TransferRecord r, LogExportOptions options)
        {
            string? html = ReadSnapshot(r.HtmlSnapshotPath, options);
            return html != null ? HtmlToMd(html) : NormText(r.Text);
        }

        private static string ToText(TransferRecord r, LogExportOptions options)
        {
            string? html = ReadSnapshot(r.HtmlSnapshotPath, options);
            return html != null ? HtmlToPlain(html) : NormText(r.Text);
        }

        private static string? ReadSnapshot(string? path, LogExportOptions options)
        {
            if (!options.PreferHtmlSnapshot) return null;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            try   { return File.ReadAllText(path, Encoding.UTF8); }
            catch { return null; }
        }

        private static string HtmlToMd(string html)
        {
            html = StripNoise(html);
            html = Regex.Replace(html, @"<br\s*/?>", "\n",   RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"</p>",       "\n\n", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<li[^>]*>",  "- ",   RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"</li>",       "\n",   RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<h1[^>]*>",  "# ",   RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<h2[^>]*>",  "## ",  RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<h3[^>]*>",  "### ", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<h4[^>]*>",  "#### ",RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<(strong|b)[^>]*>", "**", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"</(strong|b)>",     "**", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<(em|i)[^>]*>",     "*",  RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"</(em|i)>",         "*",  RegexOptions.IgnoreCase);
            return CleanText(html);
        }

        private static string HtmlToPlain(string html)
        {
            html = StripNoise(html);
            html = Regex.Replace(html, @"<br\s*/?>", "\n",   RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"</p>",       "\n\n", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"</div>",     "\n",   RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<li[^>]*>",  "- ",   RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"</li>",       "\n",   RegexOptions.IgnoreCase);
            return CleanText(html);
        }

        private static string StripNoise(string html)
        {
            html = Regex.Replace(html, @"<script\b[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<style\b[\s\S]*?</style>",   "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<button\b[\s\S]*?</button>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<svg\b[\s\S]*?</svg>",       "", RegexOptions.IgnoreCase);
            // katex-mathml を除去（katex-html は残す）
            html = Regex.Replace(html,
                @"<span[^>]*class=""[^""]*katex-mathml[^""]*""[^>]*>[\s\S]*?</span>",
                "", RegexOptions.IgnoreCase);
            return html;
        }

        private static string CleanText(string html)
        {
            html = Regex.Replace(html, @"<[^>]+>", "");
            html = WebUtility.HtmlDecode(html);
            html = html.Replace("\u00A0", " ");
            html = Regex.Replace(html, @"\r\n|\r", "\n");
            html = Regex.Replace(html, @"[ \t]+\n", "\n");
            html = Regex.Replace(html, @"\n{3,}", "\n\n");
            return html.Trim();
        }

        private static string NormText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            text = WebUtility.HtmlDecode(text);
            text = Regex.Replace(text, @"<[^>]+>", "");
            text = Regex.Replace(text, @"\r\n|\r", "\n");
            text = Regex.Replace(text, @"\n{3,}", "\n\n");
            return text.Trim();
        }

        // ─── ファイル名 / フィルター ─────────────────────────────
        private static string PerTurnFileName(TransferRecord r, string ext)
            => $"Turn{r.TurnNumber:000}_{Safe(r.SourceSite)}_to_{Safe(r.TargetSite)}.{ext}";

        private static string Ext(LogExportFormat f) => f switch
        {
            LogExportFormat.Html     => "html",
            LogExportFormat.Markdown => "md",
            _                        => "txt"
        };

        private static string Filter(LogExportFormat f) => f switch
        {
            LogExportFormat.Html     => "HTML (*.html)|*.html|All (*.*)|*.*",
            LogExportFormat.Markdown => "Markdown (*.md)|*.md|Text (*.txt)|*.txt|All (*.*)|*.*",
            _                        => "Text (*.txt)|*.txt|All (*.*)|*.*"
        };

        private static string Safe(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "Unknown";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim();
        }

        private static string He(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

        private static LogExportResult Fail(string msg)
            => new() { Success = false, Message = msg };

        private static LogExportResult Ok(string msg, string path)
            => new() { Success = true, Message = msg, OutputPaths = new List<string> { path } };
    }
}
