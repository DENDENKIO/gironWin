using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace gironWin
{
    public enum LogExportFormat
    {
        Html,
        Markdown,
        Text
    }

    public enum LogExportMode
    {
        Combined,
        Separate
    }

    public sealed class LogExportOptions
    {
        public LogExportFormat Format { get; set; } = LogExportFormat.Markdown;
        public LogExportMode Mode { get; set; } = LogExportMode.Combined;
        public bool IncludeMetadata { get; set; } = true;
        public bool PreferHtmlSnapshot { get; set; } = true;
        public string BaseFileName { get; set; } = $"giron-export-{DateTime.Now:yyyyMMdd-HHmmss}";
    }

    public sealed class LogExportResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public List<string> OutputPaths { get; init; } = new();
    }

    public sealed class ExportService
    {
        public LogExportResult ExportAiSiteHtmlTabLogs(IReadOnlyList<TransferRecord> records, LogExportOptions options)
        {
            if (records == null || records.Count == 0)
            {
                return new LogExportResult
                {
                    Success = false,
                    Message = "No logs to export."
                };
            }

            var ordered = records
                .OrderBy(x => x.TurnNumber)
                .ThenBy(x => x.Timestamp)
                .ToList();

            return options.Mode switch
            {
                LogExportMode.Combined => ExportCombined(ordered, options),
                LogExportMode.Separate => ExportSeparate(ordered, options),
                _ => new LogExportResult { Success = false, Message = "Unknown output mode." }
            };
        }

        private LogExportResult ExportCombined(List<TransferRecord> records, LogExportOptions options)
        {
            var dialog = new SaveFileDialog
            {
                FileName = options.BaseFileName,
                DefaultExt = GetExtension(options.Format),
                Filter = GetSaveFilter(options.Format),
                AddExtension = true,
                OverwritePrompt = true,
                Title = "Select combined export destination"
            };

            if (dialog.ShowDialog() != true)
            {
                return new LogExportResult { Success = false, Message = "Save cancelled." };
            }

            string content = options.Format switch
            {
                LogExportFormat.Html => BuildCombinedHtml(records, options),
                LogExportFormat.Markdown => BuildCombinedMarkdown(records, options),
                LogExportFormat.Text => BuildCombinedText(records, options),
                _ => throw new NotSupportedException()
            };

            File.WriteAllText(dialog.FileName, content, new UTF8Encoding(true));

            return new LogExportResult
            {
                Success = true,
                Message = "Combined export completed.",
                OutputPaths = new List<string> { dialog.FileName }
            };
        }

        private LogExportResult ExportSeparate(List<TransferRecord> records, LogExportOptions options)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select output folder for separate files"
            };

            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                return new LogExportResult { Success = false, Message = "Folder selection cancelled." };
            }

            string root = Path.Combine(dialog.FolderName, options.BaseFileName);
            Directory.CreateDirectory(root);

            var outputPaths = new List<string>();

            foreach (var record in records)
            {
                string ext = GetExtension(options.Format);
                string fileName = BuildPerTurnFileName(record, ext);
                string fullPath = Path.Combine(root, fileName);

                string content = options.Format switch
                {
                    LogExportFormat.Html => BuildPerTurnHtml(record, options),
                    LogExportFormat.Markdown => BuildPerTurnMarkdown(record, options),
                    LogExportFormat.Text => BuildPerTurnText(record, options),
                    _ => throw new NotSupportedException()
                };

                File.WriteAllText(fullPath, content, new UTF8Encoding(true));
                outputPaths.Add(fullPath);
            }

            return new LogExportResult
            {
                Success = true,
                Message = "Separate export completed.",
                OutputPaths = outputPaths
            };
        }

        private static string BuildCombinedHtml(List<TransferRecord> records, LogExportOptions options)
        {
            var sb = new StringBuilder();

            sb.AppendLine("<!doctype html>");
            sb.AppendLine("<html lang='ja'>");
            sb.AppendLine("<head>");
            sb.AppendLine("<meta charset='utf-8'>");
            sb.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1'>");
            sb.AppendLine("<title>AI Site HTML Export</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body{font-family:'Segoe UI',sans-serif;line-height:1.65;margin:24px;background:#f7f7f7;color:#222;}");
            sb.AppendLine("h1{margin-bottom:24px;}");
            sb.AppendLine("section{background:#fff;border:1px solid #ddd;border-radius:12px;padding:20px;margin-bottom:24px;}");
            sb.AppendLine(".meta{font-size:13px;color:#666;margin-bottom:16px;white-space:pre-wrap;}");
            sb.AppendLine(".missing{color:#b00020;font-weight:600;}");
            sb.AppendLine(".html-wrap{border:1px solid #ccc;border-radius:8px;padding:16px;background:#fff;overflow:auto;}");
            sb.AppendLine("</style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("<h1>AI Site HTML Export (All Turns)</h1>");

            foreach (var r in records)
            {
                sb.AppendLine("<section>");
                sb.AppendLine($"<h2>Turn {r.TurnNumber}: {HtmlEncode(r.SourceSite)} -> {HtmlEncode(r.TargetSite)}</h2>");

                if (options.IncludeMetadata)
                {
                    sb.AppendLine("<div class='meta'>");
                    string ts = r.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
                    sb.AppendLine($"Timestamp: {HtmlEncode(ts)}<br>");
                    sb.AppendLine($"Direction: {HtmlEncode(r.Direction ?? string.Empty)}<br>");
                    sb.AppendLine($"Summary: {HtmlEncode(r.Summary ?? string.Empty)}");
                    sb.AppendLine("</div>");
                }

                string? html = options.PreferHtmlSnapshot ? TryReadHtmlSnapshot(r.HtmlSnapshotPath) : null;
                if (string.IsNullOrWhiteSpace(html))
                {
                    sb.AppendLine("<div class='missing'>HTML snapshot not found.</div>");
                    sb.AppendLine("<pre>");
                    sb.AppendLine(HtmlEncode(NormalizePlainText(r.Text)));
                    sb.AppendLine("</pre>");
                }
                else
                {
                    sb.AppendLine("<div class='html-wrap'>");
                    sb.AppendLine(html);
                    sb.AppendLine("</div>");
                }

                sb.AppendLine("</section>");
            }

            sb.AppendLine("</body>");
            sb.AppendLine("</html>");
            return sb.ToString();
        }

        private static string BuildPerTurnHtml(TransferRecord r, LogExportOptions options)
        {
            string? html = options.PreferHtmlSnapshot ? TryReadHtmlSnapshot(r.HtmlSnapshotPath) : null;
            if (!string.IsNullOrWhiteSpace(html))
            {
                return html;
            }

            var sb = new StringBuilder();
            sb.AppendLine("<!doctype html>");
            sb.AppendLine("<html lang='ja'>");
            sb.AppendLine("<head><meta charset='utf-8'><title>HTML Snapshot Missing</title></head>");
            sb.AppendLine("<body>");
            sb.AppendLine($"<h1>Turn {r.TurnNumber}</h1>");
            sb.AppendLine("<p>HTML snapshot not found. Below is the fallback text output.</p>");
            sb.AppendLine("<pre>");
            sb.AppendLine(HtmlEncode(NormalizePlainText(r.Text)));
            sb.AppendLine("</pre>");
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private static string BuildCombinedMarkdown(List<TransferRecord> records, LogExportOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# AI Site HTML Export");
            sb.AppendLine();

            foreach (var r in records)
            {
                sb.AppendLine($"## Turn {r.TurnNumber}: {Safe(r.SourceSite)} -> {Safe(r.TargetSite)}");
                sb.AppendLine();

                if (options.IncludeMetadata)
                {
                    sb.AppendLine($"- Timestamp: {r.Timestamp:yyyy-MM-dd HH:mm:ss}");
                    sb.AppendLine($"- Direction: {Safe(r.Direction)}");
                    sb.AppendLine($"- Summary: {Safe(r.Summary)}");
                    sb.AppendLine();
                }

                sb.AppendLine(ConvertRecordToMarkdown(r, options));
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string BuildPerTurnMarkdown(TransferRecord r, LogExportOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Turn {r.TurnNumber}: {Safe(r.SourceSite)} -> {Safe(r.TargetSite)}");
            sb.AppendLine();

            if (options.IncludeMetadata)
            {
                sb.AppendLine($"- Timestamp: {r.Timestamp:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"- Direction: {Safe(r.Direction)}");
                sb.AppendLine($"- Summary: {Safe(r.Summary)}");
                sb.AppendLine();
            }

            sb.AppendLine(ConvertRecordToMarkdown(r, options));
            return sb.ToString();
        }

        private static string BuildCombinedText(List<TransferRecord> records, LogExportOptions options)
        {
            var sb = new StringBuilder();

            foreach (var r in records)
            {
                sb.AppendLine($"===== Turn {r.TurnNumber}: {Safe(r.SourceSite)} -> {Safe(r.TargetSite)} =====");

                if (options.IncludeMetadata)
                {
                    sb.AppendLine($"Timestamp: {r.Timestamp:yyyy-MM-dd HH:mm:ss}");
                    sb.AppendLine($"Direction: {Safe(r.Direction)}");
                    sb.AppendLine($"Summary: {Safe(r.Summary)}");
                    sb.AppendLine();
                }

                sb.AppendLine(ConvertRecordToText(r, options));
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string BuildPerTurnText(TransferRecord r, LogExportOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Turn {r.TurnNumber}: {Safe(r.SourceSite)} -> {Safe(r.TargetSite)}");

            if (options.IncludeMetadata)
            {
                sb.AppendLine($"Timestamp: {r.Timestamp:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Direction: {Safe(r.Direction)}");
                sb.AppendLine($"Summary: {Safe(r.Summary)}");
                sb.AppendLine();
            }

            sb.AppendLine(ConvertRecordToText(r, options));
            return sb.ToString();
        }

        private static string ConvertRecordToMarkdown(TransferRecord r, LogExportOptions options)
        {
            string? html = options.PreferHtmlSnapshot ? TryReadHtmlSnapshot(r.HtmlSnapshotPath) : null;
            if (string.IsNullOrWhiteSpace(html))
            {
                return NormalizePlainText(r.Text);
            }

            return HtmlToMarkdownLikeText(html);
        }

        private static string ConvertRecordToText(TransferRecord r, LogExportOptions options)
        {
            string? html = options.PreferHtmlSnapshot ? TryReadHtmlSnapshot(r.HtmlSnapshotPath) : null;
            if (string.IsNullOrWhiteSpace(html))
            {
                return NormalizePlainText(r.Text);
            }

            return HtmlToPlainText(html);
        }

        private static string? TryReadHtmlSnapshot(string? path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return null;
                if (!File.Exists(path)) return null;
                return File.ReadAllText(path, Encoding.UTF8);
            }
            catch
            {
                return null;
            }
        }

        private static string HtmlToMarkdownLikeText(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;

            html = RemoveNoiseBlocks(html);

            html = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"</p\s*>", "\n\n", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<li\b[^>]*>", "- ", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"</li\s*>", "\n", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<h1\b[^>]*>", "# ", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<h2\b[^>]*>", "## ", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<h3\b[^>]*>", "### ", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<h4\b[^>]*>", "#### ", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<h5\b[^>]*>", "##### ", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<h6\b[^>]*>", "###### ", RegexOptions.IgnoreCase);

            html = Regex.Replace(html, @"<(strong|b)\b[^>]*>", "**", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"</(strong|b)\s*>", "**", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<(em|i)\b[^>]*>", "*", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"</(em|i)\s*>", "*", RegexOptions.IgnoreCase);

            html = Regex.Replace(html, @"<script\b[^<]*(?:(?!</script>)<[^<]*)*</script>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<style\b[^<]*(?:(?!</style>)<[^<]*)*</style>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<[^>]+>", "");
            html = WebUtility.HtmlDecode(html);
            html = html.Replace("\u00A0", " ");
            html = Regex.Replace(html, @"\r\n|\r", "\n");
            html = Regex.Replace(html, @"[ \t]+\n", "\n");
            html = Regex.Replace(html, @"\n{3,}", "\n\n");

            return html.Trim();
        }

        private static string HtmlToPlainText(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;

            html = RemoveNoiseBlocks(html);

            html = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"</p\s*>", "\n\n", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"</div\s*>", "\n", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<li\b[^>]*>", "- ", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"</li\s*>", "\n", RegexOptions.IgnoreCase);

            html = Regex.Replace(html, @"<script\b[^<]*(?:(?!</script>)<[^<]*)*</script>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<style\b[^<]*(?:(?!</style>)<[^<]*)*</style>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<[^>]+>", "");
            html = WebUtility.HtmlDecode(html);
            html = html.Replace("\u00A0", " ");
            html = Regex.Replace(html, @"\r\n|\r", "\n");
            html = Regex.Replace(html, @"[ \t]+\n", "\n");
            html = Regex.Replace(html, @"\n{3,}", "\n\n");

            return html.Trim();
        }

        private static string RemoveNoiseBlocks(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;

            html = Regex.Replace(html, @"<button\b[^>]*>.*?</button>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<svg\b[^>]*>.*?</svg>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<img\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            html = Regex.Replace(html, @"<span\b[^>]*class=""[^""]*citation[^""]*""[^>]*>.*?</span>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<div\b[^>]*class=""[^""]*citation[^""]*""[^>]*>.*?</div>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<span\b[^>]*class=""[^""]*katex-html[^""]*""[^>]*>.*?</span>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            return html;
        }

        private static string NormalizePlainText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            text = WebUtility.HtmlDecode(text);
            text = text.Replace("\u00A0", " ");
            text = Regex.Replace(text, @"<[^>]+>", string.Empty);
            text = Regex.Replace(text, @"\r\n|\r", "\n");
            text = Regex.Replace(text, @"\n{3,}", "\n\n");

            return text.Trim();
        }

        private static string BuildPerTurnFileName(TransferRecord r, string ext)
        {
            string src = SanitizeFileName(r.SourceSite);
            string tgt = SanitizeFileName(r.TargetSite);
            return $"Turn{r.TurnNumber:000}_{src}_to_{tgt}.{ext}";
        }

        private static string GetExtension(LogExportFormat format) => format switch
        {
            LogExportFormat.Html => "html",
            LogExportFormat.Markdown => "md",
            LogExportFormat.Text => "txt",
            _ => "txt"
        };

        private static string GetSaveFilter(LogExportFormat format) => format switch
        {
            LogExportFormat.Html => "HTML file (*.html)|*.html|All files (*.*)|*.*",
            LogExportFormat.Markdown => "Markdown file (*.md)|*.md|Text file (*.txt)|*.txt|All files (*.*)|*.*",
            LogExportFormat.Text => "Text file (*.txt)|*.txt|All files (*.*)|*.*",
            _ => "All files (*.*)|*.*"
        };

        private static string SanitizeFileName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Unknown";
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(c, '_');
            }
            return value.Trim();
        }

        private static string HtmlEncode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
        private static string Safe(string? value) => value ?? string.Empty;
    }
}
