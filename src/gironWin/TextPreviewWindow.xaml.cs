using System.Windows;

namespace gironWin
{
    /// <summary>
    /// \u30bf\u30fc\u30f3\u30ed\u30b0\u306e\u30d7\u30ec\u30d3\u30e5\u30fc\u30a6\u30a3\u30f3\u30c9\u30a6\u3002
    /// \u5168\u6587\u30b3\u30d4\u30fc\u30fb\u5168\u6587\u5f15\u7528\u30fb\u9078\u629e\u5f15\u7528\u30dc\u30bf\u30f3\u3092\u6301\u3064\u3002
    /// </summary>
    public partial class TextPreviewWindow : Window
    {
        private readonly TransferRecord?    _record;
        private readonly QuoteService?      _quoteService;
        private readonly SessionRepository? _sessionRepository;
        private readonly string             _participantId;

        // \u5358\u7d14\u30d7\u30ec\u30d3\u30e5\u30fc\u7528\uff08\u65e2\u5b58\u4e92\u63db\uff09
        public TextPreviewWindow(string text)
        {
            InitializeComponent();
            ContentTextBox.Text = text;
            _participantId      = string.Empty;
        }

        // \u5f15\u7528 + \u30bb\u30c3\u30b7\u30e7\u30f3\u4fdd\u5b58\u5bfe\u5fdc\u7248
        public TextPreviewWindow(
            TransferRecord    record,
            QuoteService      quoteService,
            string            participantId      = "",
            SessionRepository? sessionRepository = null)
        {
            InitializeComponent();
            _record            = record;
            _quoteService      = quoteService;
            _participantId     = participantId;
            _sessionRepository = sessionRepository;

            ContentTextBox.Text = record.Text;
            Title = $"Turn {record.TurnNumber} [{record.Direction}]  {record.TimestampText}";
        }

        // ---------------------------------------------------------------
        // \u5168\u6587\u30b3\u30d4\u30fc
        // ---------------------------------------------------------------
        private void CopyAllButton_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(ContentTextBox.Text);
            StatusLabel.Text = "\u2705 \u30b3\u30d4\u30fc\u3057\u307e\u3057\u305f";
        }

        // ---------------------------------------------------------------
        // \u5168\u6587\u5f15\u7528
        // ---------------------------------------------------------------
        private async void QuoteFullButton_Click(object sender, RoutedEventArgs e)
        {
            if (_record == null || _quoteService == null)
            {
                Clipboard.SetText($"> {ContentTextBox.Text}");
                StatusLabel.Text = "\u2705 \u5f15\u7528\u30c6\u30ad\u30b9\u30c8\u3092\u30b3\u30d4\u30fc\u3057\u307e\u3057\u305f";
                return;
            }

            var q = _quoteService.AddFullQuote(_record, _participantId);

            // SessionRepository \u306b\u6c38\u7d9a\u5316
            if (_sessionRepository != null)
                await _sessionRepository.AppendQuoteAsync(q);

            string preview = _quoteService.BuildQuotedMessage(new[] { q }, string.Empty);
            Clipboard.SetText(preview);
            StatusLabel.Text = $"\u2705 \u5168\u6587\u5f15\u7528\u3092\u767b\u9332\u3057\u307e\u3057\u305f (Turn {_record.TurnNumber})";
        }

        // ---------------------------------------------------------------
        // \u9078\u629e\u5f15\u7528
        // ---------------------------------------------------------------
        private async void QuotePartialButton_Click(object sender, RoutedEventArgs e)
        {
            string selected = ContentTextBox.SelectedText;

            if (string.IsNullOrWhiteSpace(selected))
            {
                StatusLabel.Text = "\u26a0 \u30c6\u30ad\u30b9\u30c8\u3092\u9078\u629e\u3057\u3066\u304f\u3060\u3055\u3044";
                return;
            }

            if (_record == null || _quoteService == null)
            {
                Clipboard.SetText($"> {selected}");
                StatusLabel.Text = "\u2705 \u9078\u629e\u30c6\u30ad\u30b9\u30c8\u3092\u30b3\u30d4\u30fc\u3057\u307e\u3057\u305f";
                return;
            }

            int startIndex = ContentTextBox.SelectionStart;
            var q = _quoteService.AddPartialQuote(
                _record, _participantId, selected, startIndex);

            // SessionRepository \u306b\u6c38\u7d9a\u5316
            if (_sessionRepository != null)
                await _sessionRepository.AppendQuoteAsync(q);

            string preview = _quoteService.BuildQuotedMessage(new[] { q }, string.Empty);
            Clipboard.SetText(preview);
            StatusLabel.Text = $"\u2705 \u9078\u629e\u5f15\u7528\u3092\u767b\u9332\u3057\u307e\u3057\u305f (Turn {_record.TurnNumber})";
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
