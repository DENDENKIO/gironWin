using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Wpf;

namespace gironWin
{
    /// <summary>
    /// AIサイトの生成HTML（outerHTML）を一時フォルダに保存し、
    /// LogReader から参照できるパスを管理するストア。
    /// </summary>
    public static class HtmlSnapshotStore
    {
        // turnKey → 保存した HTML ファイルのフルパス
        private static readonly ConcurrentDictionary<string, string> _map = new();

        private static readonly string _dir = Path.Combine(
            Path.GetTempPath(), "giron_html_snapshots");

        static HtmlSnapshotStore()
        {
            try
            {
                Directory.CreateDirectory(_dir);
            }
            catch { }
        }

        // ─────────────────────────────────────────────────
        // サイト別 JS: 必ず「最後（最新）の応答ブロック」を取得する
        // ─────────────────────────────────────────────────

        /// <summary>Gemini 用 — model-response の最後のブロック</summary>
        public static readonly string GeminiExtractScript = @"
(() => {
    const selectors = [
        'wide-model-response .message-content',
        'model-response .message-content',
        'model-response',
        '[data-message-author-role=""model""]',
        '.response-container .markdown',
        '.markdown'
    ];
    for (const sel of selectors) {
        const all = Array.from(document.querySelectorAll(sel));
        if (all.length > 0) return all[all.length - 1].outerHTML;
    }
    return document.body.outerHTML;
})();";

        /// <summary>Perplexity 用 — markdown-content-* div の最後のブロック</summary>
        public static readonly string PerplexityExtractScript = @"
(() => {
    const mdAll = Array.from(document.querySelectorAll('div[id^=""markdown-content-""]'));
    if (mdAll.length > 0) return mdAll[mdAll.length - 1].outerHTML;
    const proseAll = Array.from(document.querySelectorAll('.prose'));
    if (proseAll.length > 0) return proseAll[proseAll.length - 1].outerHTML;
    const lmAll = Array.from(document.querySelectorAll('[data-renderer=""lm""]'));
    if (lmAll.length > 0) return lmAll[lmAll.length - 1].outerHTML;
    return document.body.outerHTML;
})();";

        /// <summary>汎用フォールバック — 最後の大きなテキストブロック</summary>
        public static readonly string DefaultExtractScript = @"
(() => {
    const candidates = [
        ...Array.from(document.querySelectorAll('.markdown')),
        ...Array.from(document.querySelectorAll('[data-message-author-role=""assistant""]')),
        ...Array.from(document.querySelectorAll('[data-message-author-role=""model""]')),
        ...Array.from(document.querySelectorAll('.prose'))
    ];
    if (candidates.length > 0) return candidates[candidates.length - 1].outerHTML;
    return document.body.outerHTML;
})();";

        // ─────────────────────────────────────────────────

        /// <summary>
        /// WebView2 の生成部分 HTML をキャプチャしてファイル保存する。
        /// </summary>
        public static async Task<string?> CaptureAsync(
            WebView2 webView,
            string turnKey,
            string? extractorScript = null)
        {
            if (webView?.CoreWebView2 == null) return null;

            try
            {
                // extractorScript が null の場合はデフォルト（汎用）を使う
                string script = extractorScript ?? DefaultExtractScript;

                string rawJson = await webView.ExecuteScriptAsync(script);

                // ExecuteScriptAsync は JSON エンコードして返すのでデシリアライズ
                string html = System.Text.Json.JsonSerializer.Deserialize<string>(rawJson)
                              ?? string.Empty;

                if (string.IsNullOrWhiteSpace(html)) return null;

                string fullHtml = WrapHtml(html, turnKey);

                string safeName = string.Concat(turnKey.Split(Path.GetInvalidFileNameChars()));
                string filePath = Path.Combine(_dir,
                    $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.html");

                await File.WriteAllTextAsync(filePath, fullHtml, Encoding.UTF8);

                _map[turnKey] = filePath;
                return filePath;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>既存のパスを取得</summary>
        public static string? GetPath(string turnKey)
            => _map.TryGetValue(turnKey, out string? p) ? p : null;

        /// <summary>
        /// HTML フラグメントをスタンドアロン HTML ファイルにラップ。
        /// Gemini の画像巨大化対策として img/video/iframe に最大幅制限を追加。
        /// </summary>
        private static string WrapHtml(string fragment, string title)
        {
            return $$"""
<!DOCTYPE html>
<html lang="ja">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>{{System.Net.WebUtility.HtmlEncode(title)}}</title>

  <!-- KaTeX（Gemini のテキスト数式を自動レンダリング） -->
  <link rel="stylesheet"
        href="https://cdn.jsdelivr.net/npm/katex@0.16.11/dist/katex.min.css">
  <script defer
          src="https://cdn.jsdelivr.net/npm/katex@0.16.11/dist/katex.min.js"></script>
  <script defer
          src="https://cdn.jsdelivr.net/npm/katex@0.16.11/dist/contrib/auto-render.min.js"
          onload="renderMathInElement(document.body, {
            delimiters: [
              {left: '\\\\[', right: '\\\\]', display: true},
              {left: '$$',   right: '$$',   display: true},
              {left: '\\\\(', right: '\\\\)', display: false},
              {left: '$',    right: '$',    display: false}
            ],
            throwOnError: false
          });"></script>

  <style>
    /* ベースリセット */
    *, *::before, *::after { box-sizing: border-box; }

    body {
      font-family: 'Segoe UI', 'Hiragino Sans', 'Yu Gothic UI', sans-serif;
      font-size: 14px;
      line-height: 1.75;
      color: #1a1a1a;
      background: #f9f9f9;
      max-width: 860px;
      margin: 24px auto;
      padding: 0 24px 48px;
      word-break: break-word;
    }

    /* ── 画像・メディア巨大化防止 (Gemini 対策) ── */
    img, video, iframe, embed, object, canvas {
      max-width: 100% !important;
      max-height: 480px !important;
      height: auto !important;
      object-fit: contain !important;
      display: block;
      margin: 8px 0;
    }
    [class*="thumbnail"], [class*="preview"], [class*="image-container"] {
      max-width: 100% !important;
      overflow: hidden !important;
    }

    /* ── コード ── */
    pre, code {
      background: #f0f0f0;
      border-radius: 4px;
      font-family: 'Cascadia Code', Consolas, 'Courier New', monospace;
      font-size: 13px;
    }
    code { padding: 2px 6px; }
    pre  { padding: 12px 16px; overflow-x: auto; max-height: 480px; }

    /* ── 引用 ── */
    blockquote {
      border-left: 3px solid #ccc;
      margin: 8px 0;
      padding: 4px 16px;
      color: #555;
      background: #fafafa;
    }

    /* ── テーブル ── */
    table { border-collapse: collapse; width: 100%; margin: 12px 0; }
    td, th { border: 1px solid #ddd; padding: 8px 12px; }
    th { background: #f0f0f0; font-weight: 600; }

    /* ── 見出し ── */
    h1,h2,h3,h4,h5,h6 {
      line-height: 1.3;
      margin: 16px 0 8px;
      font-weight: 600;
    }
    h1 { font-size: 1.6em; } h2 { font-size: 1.4em; }
    h3 { font-size: 1.2em; } h4 { font-size: 1.1em; }

    /* ── Gemini の固有クラスを無力化 ── */
    [style*="width:"] { max-width: 100% !important; }
    [style*="height:"] { max-height: 480px !important; }
  </style>
</head>
<body>
{{fragment}}
</body>
</html>
""";
        }

        /// <summary>古いスナップショットファイルをクリーンアップ</summary>
        public static void Cleanup(TimeSpan olderThan)
        {
            try
            {
                if (!Directory.Exists(_dir)) return;
                foreach (string f in Directory.GetFiles(_dir, "*.html"))
                {
                    if (File.GetCreationTime(f) < DateTime.Now - olderThan)
                        File.Delete(f);
                }
            }
            catch { }
        }
    }
}
