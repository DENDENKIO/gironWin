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
using System.Windows.Threading;
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

            Loaded += Reader_Loaded;
            MainTabControl.SelectionChanged += ReaderTab_SelectionChanged;
            Closed += Reader_Closed;
        }

        private Task? _bodyInitialization;
        private Task? _htmlInitialization;
        private Task? _imagesInitialization;
        private bool _readerClosing;
        private bool _bodyLoadFailed;
        private bool _quoteSending;
        private long _contentRevision;
        private long _requestSerial;
        private long _bodyRenderedRevision = -1;
        private long _htmlRenderedRevision = -1;
        private long _imagesRenderedRevision = -1;
        private long _selectionRevision = -1;
        private ulong _htmlNavigationId;
        private ulong _imagesNavigationId;
        private long _htmlNavigationRevision = -1;
        private long _imagesNavigationRevision = -1;

        private bool ReaderUnavailable => _readerClosing || _webViewsReleased
            || Dispatcher.HasShutdownStarted;

        private bool RequestIsCurrent(long request, long revision, object? tab)
            => !ReaderUnavailable && request == _requestSerial
                && revision == _contentRevision
                && ReferenceEquals(MainTabControl.SelectedItem, tab);

        private void Reader_Loaded(object sender, RoutedEventArgs e)
            => RenderCurrentRecord();

        private void Reader_Closed(object? sender, EventArgs e)
        {
            _readerClosing = true;
            _webViewReady = false;
            _requestSerial++;
            Loaded -= Reader_Loaded;
            MainTabControl.SelectionChanged -= ReaderTab_SelectionChanged;
        }

        private void ReaderTab_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != MainTabControl || !IsLoaded || ReaderUnavailable) return;
            _ = RefreshSelectedTabAsync(++_requestSerial, _contentRevision);
        }

        private void ClearQuoteSelection()
        {
            _selectionRevision = -1;
            SelectedTextBlock.Text = "（テキストを選択してください）";
            QuoteRegisterButton.IsEnabled = false;
        }

        private bool CanReadBodySelection()
            => !ReaderUnavailable && _webViewReady
                && _bodyRenderedRevision == _contentRevision
                && _records.Count > 0 && _currentIndex >= 0 && _currentIndex < _records.Count;

        private bool CanRegisterSelection()
            => CanReadBodySelection() && !_quoteSending
                && _selectionRevision == _contentRevision;

        private void SetQuoteSelection(string text, long revision)
        {
            if (!CanReadBodySelection() || revision != _contentRevision) return;
            text = text.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                ClearQuoteSelection();
                return;
            }
            _selectionRevision = revision;
            SelectedTextBlock.Text = text;
            QuoteRegisterButton.IsEnabled = CanRegisterSelection();
        }

        private async Task InitializeBodyAsync()
        {
            await BodyWebView.EnsureCoreWebView2Async();
            if (ReaderUnavailable) return;
            string assets = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebAssets");
            if (Directory.Exists(assets))
                BodyWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    VirtualHost, assets, CoreWebView2HostResourceAccessKind.Allow);
            var core = BodyWebView.CoreWebView2;
            core.WebMessageReceived -= CoreWebView2_WebMessageReceived;
            core.WebMessageReceived += CoreWebView2_WebMessageReceived;
            core.NavigationStarting -= Body_NavigationStarting;
            core.NavigationStarting += Body_NavigationStarting;
            core.DOMContentLoaded -= Body_DOMContentLoaded;
            core.DOMContentLoaded += Body_DOMContentLoaded;
            core.NavigationCompleted -= Body_NavigationCompleted;
            core.NavigationCompleted += Body_NavigationCompleted;
            NavigateBodyShell();
        }

        private void NavigateBodyShell()
        {
            if (ReaderUnavailable) return;
            _bodyLoadFailed = false;
            _webViewReady = false;
            _bodyRenderedRevision = -1;
            ClearQuoteSelection();
            string shell = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebAssets", "index.html");
            if (File.Exists(shell))
                BodyWebView.CoreWebView2.Navigate($"https://{VirtualHost}/index.html");
            else
                BodyWebView.CoreWebView2.NavigateToString(BuildShellHtml());
        }

        private void Body_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (ReaderUnavailable) return;
            _webViewReady = false;
            _bodyRenderedRevision = -1;
            ClearQuoteSelection();
        }

        private void Body_DOMContentLoaded(object? sender, CoreWebView2DOMContentLoadedEventArgs e)
        {
            if (ReaderUnavailable) return;
            _webViewReady = true;
            _bodyLoadFailed = false;
            _ = RefreshSelectedTabAsync(++_requestSerial, _contentRevision);
        }

        private void Body_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (ReaderUnavailable || e.IsSuccess) return;
            _webViewReady = false;
            _bodyLoadFailed = true;
            _bodyRenderedRevision = -1;
            ClearQuoteSelection();
            ReportDisplayFailure("本文ページ", new InvalidOperationException(e.WebErrorStatus.ToString()));
        }

        private void Html_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
            => _htmlNavigationId = e.NavigationId;

        private void Images_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
            => _imagesNavigationId = e.NavigationId;

        private void Html_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (ReaderUnavailable || e.IsSuccess || e.NavigationId != _htmlNavigationId
                || _htmlNavigationRevision != _contentRevision) return;
            _htmlRenderedRevision = -1;
            if (ReferenceEquals(MainTabControl.SelectedItem, AiHtmlTab))
                ReportDisplayFailure("AIサイトHTML", new InvalidOperationException(e.WebErrorStatus.ToString()));
        }

        private void Images_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (ReaderUnavailable || e.IsSuccess || e.NavigationId != _imagesNavigationId
                || _imagesNavigationRevision != _contentRevision) return;
            _imagesRenderedRevision = -1;
            if (ReferenceEquals(MainTabControl.SelectedItem, ImageListTab))
                ReportDisplayFailure("画像一覧", new InvalidOperationException(e.WebErrorStatus.ToString()));
        }

        private void ReportDisplayFailure(string area, Exception ex)
        {
            if (ReaderUnavailable) return;
            System.Diagnostics.Debug.WriteLine($"[LogReader/{area}] {ex}");
            QuoteStatusLabel.Text = $"{area}の表示に失敗しました。タブを選び直すと再試行します。";
        }

        private async Task RefreshSelectedTabAsync(long request, long revision)
        {
            object? tab = MainTabControl.SelectedItem;
            try
            {
                await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Loaded);
                if (!RequestIsCurrent(request, revision, tab)) return;
                var record = _records.Count > 0 && _currentIndex >= 0 && _currentIndex < _records.Count
                    ? _records[_currentIndex] : null;
                bool hasHtml = record != null && !string.IsNullOrEmpty(record.HtmlSnapshotPath)
                    && File.Exists(record.HtmlSnapshotPath);

                if (ReferenceEquals(tab, AiHtmlTab))
                {
                    if (_htmlRenderedRevision == revision) return;
                    AiHtmlWebView.Visibility = hasHtml ? Visibility.Visible : Visibility.Collapsed;
                    NoAiHtmlLabel.Visibility = hasHtml ? Visibility.Collapsed : Visibility.Visible;
                    if (!hasHtml) { _htmlRenderedRevision = revision; return; }
                    if (_htmlInitialization == null || _htmlInitialization.IsFaulted)
                        _htmlInitialization = AiHtmlWebView.EnsureCoreWebView2Async();
                    await _htmlInitialization;
                    if (!RequestIsCurrent(request, revision, tab)) return;
                    string path = record!.HtmlSnapshotPath!;
                    string html = ResolveKatexSpans(File.ReadAllText(path, Encoding.UTF8));
                    string baseUri = new Uri(Path.GetDirectoryName(Path.GetFullPath(path))!
                        + Path.DirectorySeparatorChar).AbsoluteUri;
                    string baseTag = "<base href=\"" + System.Net.WebUtility.HtmlEncode(baseUri) + "\">";
                    var head = System.Text.RegularExpressions.Regex.Match(html,
                        @"<head\b[^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    html = head.Success ? html.Insert(head.Index + head.Length, baseTag) : baseTag + html;
                    var htmlCore = AiHtmlWebView.CoreWebView2;
                    htmlCore.NavigationStarting -= Html_NavigationStarting;
                    htmlCore.NavigationStarting += Html_NavigationStarting;
                    htmlCore.NavigationCompleted -= Html_NavigationCompleted;
                    htmlCore.NavigationCompleted += Html_NavigationCompleted;
                    _htmlNavigationRevision = revision;
                    htmlCore.NavigateToString(html);
                    _htmlRenderedRevision = revision;
                }
                else if (ReferenceEquals(tab, ImageListTab))
                {
                    if (_imagesRenderedRevision == revision) return;
                    if (!hasHtml)
                    {
                        ImageListWebView.Visibility = Visibility.Collapsed;
                        NoImageLabel.Visibility = Visibility.Visible;
                        ImageCountLabel.Text = "画像なし";
                        _imagesRenderedRevision = revision;
                        return;
                    }
                    ImageListWebView.Visibility = Visibility.Visible;
                    if (_imagesInitialization == null || _imagesInitialization.IsFaulted)
                        _imagesInitialization = ImageListWebView.EnsureCoreWebView2Async();
                    await _imagesInitialization;
                    if (!RequestIsCurrent(request, revision, tab)) return;
                    var imagesCore = ImageListWebView.CoreWebView2;
                    imagesCore.NavigationStarting -= Images_NavigationStarting;
                    imagesCore.NavigationStarting += Images_NavigationStarting;
                    imagesCore.NavigationCompleted -= Images_NavigationCompleted;
                    imagesCore.NavigationCompleted += Images_NavigationCompleted;
                    _imagesNavigationRevision = revision;
                    if (RenderImageList(record!.HtmlSnapshotPath!))
                        _imagesRenderedRevision = revision;
                }
                else if (MainTabControl.SelectedIndex == 0)
                {
                    if (_bodyRenderedRevision == revision && _webViewReady) return;
                    if (_bodyInitialization == null || _bodyInitialization.IsFaulted)
                        _bodyInitialization = InitializeBodyAsync();
                    await _bodyInitialization;
                    if (!RequestIsCurrent(request, revision, tab)) return;
                    if (_bodyLoadFailed) { NavigateBodyShell(); return; }
                    if (!_webViewReady) return;
                    string raw = record?.Text ?? string.Empty;
                    bool htmlText = raw.Contains("katex-html", StringComparison.OrdinalIgnoreCase)
                        || raw.Contains("<span class=\"katex\"", StringComparison.OrdinalIgnoreCase);
                    string body = record == null ? "<p>表示できるログがありません。</p>"
                        : htmlText ? ResolveKatexSpans(raw) : BuildBodyHtmlWithDebug(raw, _mathMode);
                    await ExecuteRenderAsync(body, BuildSummaryHtml(record?.Summary ?? string.Empty),
                        !htmlText && _mathMode, revision);
                }
            }
            catch (Exception ex)
            {
                if (RequestIsCurrent(request, revision, tab)) ReportDisplayFailure("選択タブ", ex);
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
            if (!IsInitialized || MainTabControl == null || ReaderUnavailable) return;
            long revision = ++_contentRevision;
            long request = ++_requestSerial;
            ClearQuoteSelection();
            QuoteStatusLabel.Text = string.Empty;
            bool ok = _records.Count > 0 && _currentIndex >= 0 && _currentIndex < _records.Count;
            var record = ok ? _records[_currentIndex] : null;
            PrevButton.IsEnabled = ok && _currentIndex > 0;
            NextButton.IsEnabled = ok && _currentIndex < _records.Count - 1;
            HeaderTurnLabel.Text = ok ? $"Turn {record!.TurnNumber}" : "ログなし";
            HeaderDirectionLabel.Text = ok ? $"{record!.Direction}  {record.SourceSite}→{record.TargetSite}" : string.Empty;
            HeaderTimestampLabel.Text = ok ? record!.Timestamp.ToString("yyyy/MM/dd HH:mm:ss") : string.Empty;
            PageInfoLabel.Text = ok ? $"{_currentIndex + 1} / {_records.Count}" : "0 / 0";
            PromptTextBox.Text = record?.InputText ?? "(プロンプトなし)";
            _debugRaw = record?.Text ?? string.Empty;
            RawTextBox.Text = _debugRaw;
            RawLengthLabel.Text = $"{_debugRaw.Length} 文字";
            _ = RefreshSelectedTabAsync(request, revision);
        }

        private async Task ExecuteRenderAsync(string bodyHtml, string summaryHtml,
            bool mathMode, long revision)
        {
            if (ReaderUnavailable || !_webViewReady || revision != _contentRevision) return;
            string body = JsonSerializer.Serialize(bodyHtml);
            string summary = JsonSerializer.Serialize(summaryHtml);
            string math = mathMode ? "true" : "false";
            string script = $@"
(() => {{
    try {{
        if ((window.__gironDocumentRevision || 0) > {revision}) return 'stale';
        if (typeof renderContent !== 'function') return 'missing-render-function';
        if (!window.__gironSelectionInstalled) {{
            document.addEventListener('mouseup', () => {{
                const text = window.getSelection()?.toString() || '';
                window.chrome.webview.postMessage({{
                    kind: 'giron-selection', revision: window.__gironDocumentRevision || -1, text
                }});
            }});
            window.__gironSelectionInstalled = true;
        }}
        window.getSelection()?.removeAllRanges();
        renderContent({body}, {summary}, {math});
        window.__gironDocumentRevision = {revision};
        return 'ok';
    }} catch (error) {{ return 'error:' + String(error); }}
}})();";
            string result = await BodyWebView.CoreWebView2.ExecuteScriptAsync(script);
            if (ReaderUnavailable || revision != _contentRevision) return;
            string status = JsonSerializer.Deserialize<string>(result) ?? "null";
            if (status == "stale") return;
            if (status != "ok") throw new InvalidOperationException($"本文描画失敗: {status}");
            _bodyRenderedRevision = revision;
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
            if (!CanReadBodySelection()) return;
            try
            {
                using var message = JsonDocument.Parse(e.WebMessageAsJson);
                var root = message.RootElement;
                // Legacy unversioned "sel:" messages cannot establish record identity.
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("kind", out var kind)
                    || kind.GetString() != "giron-selection"
                    || !root.TryGetProperty("revision", out var revision)
                    || !revision.TryGetInt64(out long value)
                    || !root.TryGetProperty("text", out var text)
                    || text.ValueKind != JsonValueKind.String) return;
                SetQuoteSelection(text.GetString() ?? string.Empty, value);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LogReader selection] {ex}");
            }
        }

        private async void GetSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CanReadBodySelection() || MainTabControl.SelectedIndex != 0)
            {
                QuoteStatusLabel.Text = "現在の本文の表示完了後に選択してください。";
                return;
            }
            long revision = _contentRevision;
            try
            {
                string json = await BodyWebView.CoreWebView2.ExecuteScriptAsync(@"
(() => ({ revision: window.__gironDocumentRevision || -1,
           text: window.getSelection()?.toString() || '' }))();");
                if (ReaderUnavailable || revision != _contentRevision || !CanReadBodySelection()) return;
                using var message = JsonDocument.Parse(json);
                var root = message.RootElement;
                if (root.TryGetProperty("revision", out var pageRevision)
                    && pageRevision.TryGetInt64(out long value) && value == revision
                    && root.TryGetProperty("text", out var text)
                    && text.ValueKind == JsonValueKind.String)
                    SetQuoteSelection(text.GetString() ?? string.Empty, revision);
            }
            catch (Exception ex)
            {
                if (!ReaderUnavailable && revision == _contentRevision)
                    ReportDisplayFailure("選択取得", ex);
            }
        }

        private void MathToggle_Changed(object sender, RoutedEventArgs e) { _mathMode = MathToggle.IsChecked == true; RenderCurrentRecord(); }
        private void PrevButton_Click(object sender, RoutedEventArgs e) { if (_currentIndex > 0) { _currentIndex--; RenderCurrentRecord(); } }
        private void NextButton_Click(object sender, RoutedEventArgs e) { if (_currentIndex < _records.Count - 1) { _currentIndex++; RenderCurrentRecord(); } }

        private async void QuoteRegisterButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CanRegisterSelection()) return;
            string selected = SelectedTextBlock.Text.Trim();
            if (string.IsNullOrWhiteSpace(selected) || selected == "（テキストを選択してください）") return;
            long revision = _contentRevision;
            var record = _records[_currentIndex];
            string target = (QuoteTargetCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Left";
            _quoteSending = true;
            QuoteRegisterButton.IsEnabled = false;
            try
            {
                if (target == "Clipboard")
                {
                    Clipboard.SetText(selected);
                    QuoteStatusLabel.Text = "コピーしました";
                }
                else if (!string.IsNullOrEmpty(_pipeId))
                {
                    bool ok = await SendQuoteAsync(new QuoteCallbackPayload
                    {
                        SourceTurnNumber = record.TurnNumber, QuotedText = selected,
                        TargetSeat = target, Direction = record.Direction
                    });
                    if (!ReaderUnavailable && revision == _contentRevision)
                        QuoteStatusLabel.Text = ok ? $"{target}へ登録しました" : "送信失敗";
                }
            }
            catch (Exception ex)
            {
                if (!ReaderUnavailable && revision == _contentRevision)
                    ReportDisplayFailure("引用登録", ex);
            }
            finally
            {
                _quoteSending = false;
                if (!ReaderUnavailable) QuoteRegisterButton.IsEnabled = CanRegisterSelection();
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

        private bool RenderImageList(string htmlSnapshotPath)
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
                    return true;
                }

                ImageListWebView.Visibility = Visibility.Visible;
                NoImageLabel.Visibility     = Visibility.Collapsed;
                ImageCountLabel.Text        = $"画像 {srcList.Count} 枚";

                string listHtml = BuildImageListHtml(srcList, htmlSnapshotPath);
                ImageListWebView.CoreWebView2.NavigateToString(listHtml);
                return true;
            }
            catch (Exception ex)
            {
                ImageListWebView.Visibility = Visibility.Collapsed;
                NoImageLabel.Visibility = Visibility.Visible;
                ImageCountLabel.Text = "画像の読み込みに失敗しました";
                ReportDisplayFailure("画像一覧", ex);
                return false;
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

