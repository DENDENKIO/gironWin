using Microsoft.Web.WebView2.Wpf;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace gironWin
{
    /// <summary>
    /// AIの発言に含まれる数式 ($...$ や $$...$$) を KaTeX で美しくレンダリングするためのユーティリティ。
    /// </summary>
    public static class MathRenderer
    {
        /// <summary>
        /// テキスト中の $...$ (インライン) と $$...$$ (ブロック) をKaTeXでレンダリングするHTMLを生成
        /// </summary>
        public static string BuildHtml(string text, bool darkMode = true)
        {
            if (string.IsNullOrEmpty(text)) return "<html><body></body></html>";

            string bg     = darkMode ? "#1e1e1e" : "#ffffff";
            string fg     = darkMode ? "#d4d4d4" : "#1a1a1a";
            string codeBg = darkMode ? "#2d2d2d" : "#f0f0f0";

            string bodyContent = ConvertToHtml(text);

            return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <link rel=""stylesheet"" href=""https://cdn.jsdelivr.net/npm/katex@0.16.10/dist/katex.min.css"">
    <script defer src=""https://cdn.jsdelivr.net/npm/katex@0.16.10/dist/katex.min.js""></script>
    <script defer src=""https://cdn.jsdelivr.net/npm/katex@0.16.10/dist/contrib/auto-render.min.js""
            onload=""renderMathInElement(document.body, {{
                delimiters: [
                    {{left: '$$', right: '$$', display: true}},
                    {{left: '$',  right: '$',  display: false}},
                    {{left: '\\\\[', right: '\\\\]', display: true}},
                    {{left: '\\\\(', right: '\\\\)', display: false}}
                ],
                throwOnError: false
            }});""></script>
    <style>
        * {{ box-sizing: border-box; margin: 0; padding: 0; }}
        body {{
            background: {bg};
            color: {fg};
            font-family: 'Yu Gothic UI', 'Meiryo UI', 'Segoe UI', sans-serif;
            font-size: 13px;
            line-height: 1.8;
            padding: 12px 16px;
            word-break: break-word;
        }}
        /* ブロック数式 */
        .katex-display {{
            margin: 12px 0;
            padding: 10px;
            background: {codeBg};
            border-radius: 4px;
            overflow-x: auto;
        }}
        /* インライン数式 */
        .katex {{ font-size: 1.1em; }}
        /* コードブロック */
        code, pre {{
            font-family: 'Consolas', 'Cascadia Code', monospace;
            background: {codeBg};
            border-radius: 3px;
            padding: 2px 5px;
            font-size: 12px;
        }}
        pre {{ padding: 10px; overflow-x: auto; margin: 8px 0; }}
        /* 見出し */
        h1, h2, h3 {{ margin: 10px 0 4px; font-size: 14px; font-weight: bold; }}
        h1 {{ border-bottom: 1px solid #555; padding-bottom: 4px; }}
        /* 段落 */
        p {{ margin: 4px 0; }}
        /* 水平線 */
        hr {{ border: none; border-top: 1px solid #444; margin: 10px 0; }}
    </style>
</head>
<body>
    {bodyContent}
</body>
</html>";
        }

        /// <summary>
        /// 改行と基本的なマークダウン（見出し・コードブロック）を簡易変換。
        /// 数式の $...$ / $$...$$ は KaTeX auto-render がブラウザ側で処理するためそのまま渡す。
        /// </summary>
        private static string ConvertToHtml(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            var sb = new StringBuilder();
            bool inCodeBlock = false;

            foreach (var rawLine in lines)
            {
                string line = rawLine;

                // コードブロック開始/終了
                if (line.TrimStart().StartsWith("```"))
                {
                    if (inCodeBlock) { sb.AppendLine("</code></pre>"); inCodeBlock = false; }
                    else             { sb.AppendLine("<pre><code>"); inCodeBlock = true; }
                    continue;
                }

                if (inCodeBlock)
                {
                    sb.AppendLine(HttpUtility.HtmlEncode(line));
                    continue;
                }

                // 見出し
                if (line.StartsWith("### "))
                    { sb.AppendLine($"<h3>{SimpleMarkdown(line[4..])}</h3>"); continue; }
                if (line.StartsWith("## "))
                    { sb.AppendLine($"<h2>{SimpleMarkdown(line[3..])}</h2>"); continue; }
                if (line.StartsWith("# "))
                    { sb.AppendLine($"<h1>{SimpleMarkdown(line[2..])}</h1>"); continue; }

                // 水平線
                if (line.Trim() == "---")
                    { sb.AppendLine("<hr>"); continue; }

                // 空行
                if (string.IsNullOrWhiteSpace(line))
                    { sb.AppendLine("<br>"); continue; }

                // 通常行
                sb.AppendLine($"<p>{SimpleMarkdown(line)}</p>");
            }

            if (inCodeBlock) sb.AppendLine("</code></pre>");
            return sb.ToString();
        }

        /// <summary>
        /// $...$ と $$...$$ の外側だけ最小限のエスケープを行う
        /// </summary>
        private static string SimpleMarkdown(string line)
        {
            // ★ $ をエスケープせずに KaTeX に渡す
            // HtmlEncode は & < > " だけ手動で置換する
            var sb = new StringBuilder(line.Length);
            foreach (char c in line)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    default: sb.Append(c); break;
                    // ★ '$' はそのまま通す（KaTeX が解釈する）
                }
            }
            string escaped = sb.ToString();

            // 太字 **text** -> <b>text</b>
            while (escaped.Contains("**"))
            {
                int first = escaped.IndexOf("**");
                int second = escaped.IndexOf("**", first + 2);
                if (second < 0) break;
                string content = escaped.Substring(first + 2, second - first - 2);
                escaped = escaped.Remove(first, second - first + 2)
                                 .Insert(first, $"<b>{content}</b>");
            }

            return escaped;
        }

        /// <summary>
        /// WebView2 に HTML をロードする
        /// </summary>
        public static async Task RenderToWebViewAsync(WebView2 webView, string text, bool darkMode = true)
        {
            string html = BuildHtml(text, darkMode);
            await webView.EnsureCoreWebView2Async();
            webView.NavigateToString(html);
        }
    }
}
