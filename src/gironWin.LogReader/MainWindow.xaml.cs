using gironWin.Shared;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace gironWin.LogReader
{
    public partial class MainWindow : Window
    {
        private List<TransferRecord> _records      = new();
        private int                  _currentIndex = 0;
        private bool                 _mathMode     = true;
        private string               _pipeId       = string.Empty;
        private bool                 _webViewReady = false;

        // ─── デバッグ用フィールド ───
        private string _debugRaw        = string.Empty;
        private string _debugNormalized = string.Empty;
        private string _debugHtml       = string.Empty;
        private string _debugMathBlocks = string.Empty;
        private int    _debugMathCount  = 0;

        // ─────────────────────────────────────────────────
        // 仮想ホスト名（KaTeXローカルアセット用）
        // WebAssets/ フォルダをビルド出力に含めること
        // ─────────────────────────────────────────────────
        private const string VirtualHost = "katex.local";

        public MainWindow(string jsonPath)
        {
            InitializeComponent();

            if (!string.IsNullOrEmpty(jsonPath) && File.Exists(jsonPath))
            {
                try
                {
                    string json    = File.ReadAllText(jsonPath, Encoding.UTF8);
                    var    payload = JsonSerializer.Deserialize<LogReaderPayload>(json);
                    if (payload != null)
                    {
                        _records      = payload.Records ?? new();
                        _currentIndex = Math.Clamp(
                            payload.StartIndex, 0,
                            Math.Max(0, _records.Count - 1));
                        _pipeId = payload.PipeId ?? string.Empty;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LogReader] JSON読み込み失敗: {ex.Message}");
                }
                finally
                {
                    try { File.Delete(jsonPath); } catch { }
                }
            }

            Loaded += async (s, e) => await InitWebViewAsync();
        }

        private async Task InitWebViewAsync()
        {
            try
            {
                await BodyWebView.EnsureCoreWebView2Async();
                await AiHtmlWebView.EnsureCoreWebView2Async();
                await ImageListWebView.EnsureCoreWebView2Async();  // ★追加

                string exeDir      = Path.GetDirectoryName(
                                         Assembly.GetExecutingAssembly().Location)!;
                string assetsDir   = Path.Combine(exeDir, "WebAssets");

                if (Directory.Exists(assetsDir))
                {
                    BodyWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                        VirtualHost,
                        assetsDir,
                        CoreWebView2HostResourceAccessKind.Allow);
                }

                BodyWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

                BodyWebView.CoreWebView2.DOMContentLoaded += (_, _) =>
                {
                    _webViewReady = true;
                    Dispatcher.Invoke(RenderCurrentRecord);
                };

                string shellPath = Path.Combine(assetsDir, "index.html");
                if (File.Exists(shellPath))
                    BodyWebView.CoreWebView2.Navigate($"https://{VirtualHost}/index.html");
                else
                    BodyWebView.CoreWebView2.NavigateToString(BuildShellHtml());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"WebView2初期化失敗:\n{ex.Message}", "WebView2エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string BuildShellHtml()
        {
            return """
                <!DOCTYPE html>
                <html lang='ja'>
                <head>
                  <meta charset='utf-8'>
                  <link rel='stylesheet' href='https://cdn.jsdelivr.net/npm/katex@0.16.11/dist/katex.min.css'>
                  <style>
                    body { font-family: 'Yu Mincho','Meiryo',serif; font-size: 17px; line-height: 2.0; padding: 24px 32px; color: #1a1a1a; background: #fff; }
                    .summary { background: #eef4ff; border-left: 4px solid #4477cc; padding: 10px 16px; margin-bottom: 20px; font-size: 14px; border-radius: 0 6px 6px 0; }
                    .content { max-width: 880px; word-break: break-word; }
                    .h1 { font-size:22px; font-weight:bold; margin:24px 0 10px; border-bottom:2px solid #aac; padding-bottom:4px; }
                    .h2 { font-size:19px; font-weight:bold; margin:18px 0 8px; border-bottom:1px solid #ccd; padding-bottom:2px; }
                    .h3 { font-size:17px; font-weight:bold; margin:14px 0 6px; }
                    .bullet { padding-left:1.6em; text-indent:-1.2em; margin:3px 0; }
                    .blank  { height:0.7em; }
                    code { background:#f3f3f3; padding:1px 5px; border-radius:3px; font-family:'Consolas',monospace; font-size:14px; }
                    .katex { font-size: 1.1em; }
                    .katex-display { display:block; margin:16px auto; text-align:center; overflow-x:auto; }

                    /* ★追加: MathML部分を視覚的に隠す（HTML側だけ表示） */
                    .katex .katex-mathml {
                      position: absolute;
                      width: 1px;
                      height: 1px;
                      padding: 0;
                      margin: -1px;
                      overflow: hidden;
                      clip: rect(0, 0, 0, 0);
                      white-space: nowrap;
                      border: 0;
                    }
                    /* 本文中の画像: 最大幅を制限して表示する */
                    img { max-width: 100%; height: auto; display: block; margin: 8px 0; border-radius: 4px; }
                  </style>
                </head>
                <body>
                  <div id='summary-area'></div>
                  <div class='content' id='content'></div>
                  <script>
                    function renderContent(bodyHtml, summaryHtml, mathMode) {
                      document.getElementById('content').innerHTML = bodyHtml;
                      document.getElementById('summary-area').innerHTML = summaryHtml;
                    }
                    document.addEventListener('mouseup', function() {
                      var sel = window.getSelection().toString();
                      if (sel && window.chrome && window.chrome.webview)
                        window.chrome.webview.postMessage('sel:' + sel);
                    });
                  </script>
                </body>
                </html>
                """;
        }

        private void RenderCurrentRecord()
        {
            if (!_webViewReady) return;

            bool ok = _records.Count > 0 && _currentIndex >= 0 && _currentIndex < _records.Count;

            PrevButton.IsEnabled = ok && _currentIndex > 0;
            NextButton.IsEnabled = ok && _currentIndex < _records.Count - 1;

            SelectedTextBlock.Text        = "（テキストを選択してください）";
            QuoteRegisterButton.IsEnabled = false;
            QuoteStatusLabel.Text         = string.Empty;

            if (!ok)
            {
                HeaderTurnLabel.Text      = "ログなし";
                HeaderDirectionLabel.Text = string.Empty;
                HeaderTimestampLabel.Text = string.Empty;
                PageInfoLabel.Text        = "0 / 0";
                _ = ExecuteRenderAsync("<p>表示できるログがありません。</p>", "", false);
                return;
            }

            var rec = _records[_currentIndex];
            HeaderTurnLabel.Text      = $"Turn {rec.TurnNumber}";
            HeaderDirectionLabel.Text = $"{rec.Direction}  {rec.SourceSite}→{rec.TargetSite}";
            HeaderTimestampLabel.Text = rec.Timestamp.ToString("yyyy/MM/dd HH:mm:ss");
            PageInfoLabel.Text        = $"{_currentIndex + 1} / {_records.Count}";

            // AIサイトHTML
            if (!string.IsNullOrEmpty(rec.HtmlSnapshotPath) && File.Exists(rec.HtmlSnapshotPath))
            {
                AiHtmlWebView.Visibility = Visibility.Visible;
                NoAiHtmlLabel.Visibility = Visibility.Collapsed;

                // ★修正: ファイルを直接開かず、katex-html 除去してから表示
                try
                {
                    string rawHtml     = File.ReadAllText(rec.HtmlSnapshotPath, Encoding.UTF8);
                    string filteredHtml = ResolveKatexSpans(rawHtml);

                    // 相対パス（画像・CSS等）を絶対パスに解決するため base タグを注入
                    string baseDir = Path.GetDirectoryName(rec.HtmlSnapshotPath)!
                                         .Replace('\\', '/');
                    string baseTag = $"<base href=\"file:///{baseDir}/\">";

                    // <head> の直後に base タグを挿入
                    int headClose = filteredHtml.IndexOf("<head>", StringComparison.OrdinalIgnoreCase);
                    if (headClose >= 0)
                    {
                        int insertAt = headClose + "<head>".Length;
                        filteredHtml = filteredHtml.Insert(insertAt, baseTag);
                    }
                    else
                    {
                        filteredHtml = baseTag + filteredHtml;
                    }

                    AiHtmlWebView.CoreWebView2.NavigateToString(filteredHtml);
                }
                catch
                {
                    // フォールバック: フィルタなしで直接開く
                    AiHtmlWebView.Source = new Uri(rec.HtmlSnapshotPath);
                }

                // 画像一覧を生成
                RenderImageList(rec.HtmlSnapshotPath);
            }
            else
            {
                AiHtmlWebView.Visibility = Visibility.Collapsed;
                NoAiHtmlLabel.Visibility = Visibility.Visible;

                // ★追加: 画像なし状態にリセット
                ImageListWebView.Visibility = Visibility.Collapsed;
                NoImageLabel.Visibility     = Visibility.Visible;
                ImageCountLabel.Text        = "画像なし";
            }

            // 送信プロンプト
            PromptTextBox.Text = rec.InputText ?? "(プロンプトなし)";

            // RAWテキスト
            string rawText = rec.Text ?? string.Empty;
            RawTextBox.Text = rawText;
            RawLengthLabel.Text = $"{rawText.Length} 文字";

            bool isAlreadyHtml = rawText.Contains("katex-html", StringComparison.OrdinalIgnoreCase)
                              || rawText.Contains("<span class=\"katex\"", StringComparison.OrdinalIgnoreCase);

            string bodyHtml;
            bool   effectiveMathMode;

            if (isAlreadyHtml)
            {
                // ★修正: katex-mathml 除去、katex-html 保持
                bodyHtml = ResolveKatexSpans(rawText);
                effectiveMathMode = false;
            }
            else
            {
                bodyHtml = BuildBodyHtmlWithDebug(rawText, _mathMode);
                effectiveMathMode = _mathMode;
            }

            string summaryHtml = BuildSummaryHtml(rec.Summary ?? string.Empty);
            _ = ExecuteRenderAsync(bodyHtml, summaryHtml, effectiveMathMode);
            UpdateDebugPanel();
        }

        private async Task ExecuteRenderAsync(string bodyHtml, string summaryHtml, bool mathMode)
        {
            if (!_webViewReady) return;
            try
            {
                string safeBody    = JsonSerializer.Serialize(bodyHtml);
                string safeSummary = JsonSerializer.Serialize(summaryHtml);
                string safeMath    = mathMode ? "true" : "false";
                string script = $"renderContent({safeBody},{safeSummary},{safeMath});";
                await BodyWebView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch { }
        }

        private static string BuildBodyHtml(string text, bool mathMode)
        {
            string normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
            var (safeText, mathBlocks) = ExtractMathBlocks(normalized);
            string escaped = safeText.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            string restored = RestoreMathBlocks(escaped, mathBlocks);
            return ProcessMarkdown(restored);
        }

        private static string BuildSummaryHtml(string summary)
        {
            if (string.IsNullOrWhiteSpace(summary)) return "";
            string escaped = summary.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            return $"<div class='summary'><b>【要約】</b>{escaped}</div>";
        }

        private static (string text, List<string> blocks) ExtractMathBlocks(string text)
        {
            var blocks = new List<string>();
            var sb     = new StringBuilder();
            int i      = 0;
            while (i < text.Length)
            {
                if (i + 1 < text.Length && text[i] == '\\' && (text[i + 1] == '[' || text[i + 1] == '('))
                {
                    string close = text[i + 1] == '[' ? @"\]" : @"\)";
                    int end = text.IndexOf(close, i + 2, StringComparison.Ordinal);
                    if (end >= 0) { sb.Append($"\x00MATH{blocks.Count}\x00"); blocks.Add(text[i..(end + 2)]); i = end + 2; continue; }
                }
                else if (i + 1 < text.Length && text[i] == '$' && text[i + 1] == '$')
                {
                    int end = text.IndexOf("$$", i + 2, StringComparison.Ordinal);
                    if (end >= 0) { sb.Append($"\x00MATH{blocks.Count}\x00"); blocks.Add(text[i..(end + 2)]); i = end + 2; continue; }
                }
                else if (text[i] == '$')
                {
                    int end = text.IndexOf('$', i + 1);
                    if (end > i + 1 && !text[(i + 1)..end].Contains('\n'))
                    { sb.Append($"\x00MATH{blocks.Count}\x00"); blocks.Add(text[i..(end + 1)]); i = end + 1; continue; }
                }
                sb.Append(text[i]); i++;
            }
            return (sb.ToString(), blocks);
        }

        private static string RestoreMathBlocks(string text, List<string> blocks)
        {
            for (int i = 0; i < blocks.Count; i++) text = text.Replace($"\x00MATH{i}\x00", blocks[i]);
            return text;
        }

        private static string ProcessMarkdown(string text)
        {
            var sb    = new StringBuilder();
            var lines = text.Split('\n');
            var para  = new StringBuilder(); // 段落バッファ

            void FlushPara()
            {
                string t = para.ToString().Trim();
                if (!string.IsNullOrEmpty(t))
                {
                    // 段落内の改行を <br> に変換
                    sb.AppendLine($"<p>{t.Replace("\n", "<br>")}</p>");
                }
                para.Clear();
            }

            foreach (string raw in lines)
            {
                string line = raw.TrimEnd();

                if (line.StartsWith("### "))
                {
                    FlushPara();
                    sb.AppendLine($"<div class='h3'>{ApplyInline(line[4..])}</div>");
                }
                else if (line.StartsWith("## "))
                {
                    FlushPara();
                    sb.AppendLine($"<div class='h2'>{ApplyInline(line[3..])}</div>");
                }
                else if (line.StartsWith("# "))
                {
                    FlushPara();
                    sb.AppendLine($"<div class='h1'>{ApplyInline(line[2..])}</div>");
                }
                else if (line.StartsWith("- ") || line.StartsWith("* "))
                {
                    FlushPara();
                    sb.AppendLine($"<div class='bullet'>• {ApplyInline(line[2..])}</div>");
                }
                else if (string.IsNullOrWhiteSpace(line))
                {
                    FlushPara(); // 空行で段落を区切る
                }
                else
                {
                    // 段落バッファに追加
                    if (para.Length > 0) para.Append('\n');
                    para.Append(ApplyInline(line));
                }
            }
            FlushPara(); // 末尾フラッシュ

            return sb.ToString();
        }

        private static string ApplyInline(string s)
        {
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\*\*(.+?)\*\*", "<b>$1</b>");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"`(.+?)`", "<code>$1</code>");
            return s;
        }

        private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string msg = e.TryGetWebMessageAsString();
                if (msg.StartsWith("sel:"))
                {
                    string sel = msg["sel:".Length..].Trim();
                    if (!string.IsNullOrWhiteSpace(sel)) { SelectedTextBlock.Text = sel; QuoteRegisterButton.IsEnabled = true; }
                }
            }
            catch { }
        }

        private async void GetSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_webViewReady) return;
            try
            {
                string raw = await BodyWebView.CoreWebView2.ExecuteScriptAsync("window.getSelection().toString()");
                string sel = raw.Trim('"').Replace("\\n", "\n").Replace("\\r", "").Trim();
                if (!string.IsNullOrWhiteSpace(sel)) { SelectedTextBlock.Text = sel; QuoteRegisterButton.IsEnabled = true; }
            }
            catch { }
        }

        private void MathToggle_Changed(object sender, RoutedEventArgs e) { _mathMode = MathToggle.IsChecked == true; RenderCurrentRecord(); }
        private void PrevButton_Click(object sender, RoutedEventArgs e) { if (_currentIndex > 0) { _currentIndex--; RenderCurrentRecord(); } }
        private void NextButton_Click(object sender, RoutedEventArgs e) { if (_currentIndex < _records.Count - 1) { _currentIndex++; RenderCurrentRecord(); } }

        private async void QuoteRegisterButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIndex < 0 || _currentIndex >= _records.Count) return;
            string selected = SelectedTextBlock.Text.Trim();
            if (string.IsNullOrWhiteSpace(selected) || selected == "（テキストを選択してください）") return;

            var rec = _records[_currentIndex];
            string target = (QuoteTargetCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Left";

            if (target == "Clipboard") { Clipboard.SetText(selected); QuoteStatusLabel.Text = "✔ コピーしました"; return; }

            if (!string.IsNullOrEmpty(_pipeId))
            {
                bool ok = await SendQuoteAsync(new QuoteCallbackPayload { SourceTurnNumber = rec.TurnNumber, QuotedText = selected, TargetSeat = target, Direction = rec.Direction });
                QuoteStatusLabel.Text = ok ? $"✔ {target}へ登録しました" : "⚠ 送信失敗";
            }
        }

        private async Task<bool> SendQuoteAsync(QuoteCallbackPayload payload)
        {
            try
            {
                await using var pipe = new NamedPipeClientStream(".", $"giron_quote_{_pipeId}", PipeDirection.Out, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(2000);
                byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
                await pipe.WriteAsync(bytes);
                return true;
            }
            catch { return false; }
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e) { if (_records.Count > 0) Clipboard.SetText(_records[_currentIndex].Text ?? ""); }
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void RenderImageList(string htmlSnapshotPath)
        {
            try
            {
                string html = File.ReadAllText(htmlSnapshotPath, Encoding.UTF8);

                // <img src="..."> を正規表現で全抽出
                var imgMatches = System.Text.RegularExpressions.Regex.Matches(
                    html,
                    @"<img\s[^>]*src\s*=\s*[""']([^""']+)[""'][^>]*>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                var srcList = new List<string>();
                foreach (System.Text.RegularExpressions.Match m in imgMatches)
                {
                    string src = m.Groups[1].Value.Trim();
                    if (string.IsNullOrWhiteSpace(src)) continue;
                    // data URI・1px追跡ピクセル系を除外
                    if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                    srcList.Add(src);
                }

                if (srcList.Count == 0)
                {
                    ImageListWebView.Visibility = Visibility.Collapsed;
                    NoImageLabel.Visibility     = Visibility.Visible;
                    ImageCountLabel.Text        = "画像なし";
                    return;
                }

                ImageListWebView.Visibility = Visibility.Visible;
                NoImageLabel.Visibility     = Visibility.Collapsed;
                ImageCountLabel.Text        = $"画像 {srcList.Count} 枚";

                string listHtml = BuildImageListHtml(srcList, htmlSnapshotPath);
                ImageListWebView.CoreWebView2.NavigateToString(listHtml);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageList] 失敗: {ex.Message}");
            }
        }

        private static string BuildImageListHtml(List<string> srcList, string baseFilePath)
        {
            // 相対パスを絶対パス(file:///)に変換するためのベースディレクトリ
            string baseDir = Path.GetDirectoryName(baseFilePath) ?? string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("""
                <!DOCTYPE html>
                <html lang='ja'>
                <head>
                  <meta charset='utf-8'>
                  <style>
                    body {
                      margin: 0; padding: 16px;
                      background: #f5f5f5;
                      font-family: 'Yu Gothic UI', 'Meiryo', sans-serif;
                    }
                    .grid {
                      display: flex;
                      flex-wrap: wrap;
                      gap: 12px;
                    }
                    .img-card {
                      background: #fff;
                      border: 1px solid #ddd;
                      border-radius: 6px;
                      padding: 8px;
                      width: 240px;
                      box-shadow: 0 1px 4px rgba(0,0,0,0.08);
                    }
                    .img-card img {
                      width: 224px;
                      height: 168px;
                      object-fit: contain;
                      background: #eee;
                      display: block;
                      border-radius: 3px;
                    }
                    .img-card .caption {
                      font-size: 10px;
                      color: #888;
                      margin-top: 6px;
                      word-break: break-all;
                      overflow: hidden;
                      text-overflow: ellipsis;
                      white-space: nowrap;
                    }
                  </style>
                </head>
                <body>
                  <div class='grid'>
                """);

            foreach (string src in srcList)
            {
                // 相対URLを絶対file:///パスに変換
                string displaySrc = src;
                if (!src.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    && !src.StartsWith("//", StringComparison.OrdinalIgnoreCase)
                    && !src.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                {
                    string abs = Path.GetFullPath(Path.Combine(baseDir, src));
                    displaySrc = new Uri(abs).AbsoluteUri;
                }

                string escapedSrc     = System.Net.WebUtility.HtmlEncode(displaySrc);
                string captionEncoded = System.Net.WebUtility.HtmlEncode(src);

                sb.AppendLine($"""
                    <div class='img-card'>
                      <img src='{escapedSrc}' alt='' loading='lazy'
                           onerror="this.style.background='#ddd';this.removeAttribute('src')"/>
                      <div class='caption' title='{escapedSrc}'>{captionEncoded}</div>
                    </div>
                    """);
            }

            sb.AppendLine("""
                  </div>
                </body>
                </html>
                """);

            return sb.ToString();
        }



        /// <summary>
        /// katex-html の aria-hidden="true" を除去して表示対象にする。
        /// </summary>
        private static string ResolveKatexSpans(string html)
        {
            if (string.IsNullOrEmpty(html)) return html;

            // katex-html の aria-hidden="true" を除去
            html = System.Text.RegularExpressions.Regex.Replace(
                html,
                @"(<span\s[^>]*class=[""']katex-html[""'][^>]*)\s+aria-hidden=[""']true[""']",
                "$1",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            return html;
        }

        private string BuildBodyHtmlWithDebug(string text, bool mathMode)
        {
            _debugRaw = text;
            string normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
            var (safeText, mathBlocks) = ExtractMathBlocks(normalized);
            _debugMathCount = mathBlocks.Count;
            string escaped = safeText.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            string restored = RestoreMathBlocks(escaped, mathBlocks);
            return ProcessMarkdown(restored);
        }

        private void UpdateDebugPanel()
        {
            if (DebugToggle.IsChecked != true) return;
            Dispatcher.Invoke(() => { RawTextBox.Text = _debugRaw; RawLengthLabel.Text = $"{_debugRaw.Length} 文字"; });
        }

        private void DebugToggle_Changed(object sender, RoutedEventArgs e) { if (DebugToggle.IsChecked == true) UpdateDebugPanel(); }

        // ─── エクスポート ─────────────────────────────────────────────
        private readonly ExportService _exportService = new();

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_records.Count == 0)
            {
                MessageBox.Show(
                    "表示中のログがありません。",
                    "エクスポート",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var dlg = new LogExportOptionsDialog { Owner = this };
            if (dlg.ShowDialog() != true || dlg.Options == null)
                return;

            LogExportResult result;
            try
            {
                result = _exportService.Export(_records, dlg.Options);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"エクスポート中にエラーが発生しました。\n{ex.Message}",
                    "エクスポート",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            MessageBox.Show(
                result.Message,
                "エクスポート",
                MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
    }
}
