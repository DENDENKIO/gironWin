using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using gironWin.Shared;

namespace gironWin
{
    public partial class TextPreviewWindow : Window
    {
        private readonly List<TransferRecord> _records;
        private readonly QuoteService? _quoteService;
        private int _currentIndex;

        public TextPreviewWindow(IReadOnlyList<TransferRecord> records, int startIndex = 0, QuoteService? quoteService = null)
        {
            InitializeComponent();
            _records = records.ToList();
            _currentIndex = startIndex;
            _quoteService = quoteService;
            RenderCurrentRecord();
        }

        private void RenderCurrentRecord()
        {
            if (_records == null || _records.Count == 0 || _currentIndex < 0 || _currentIndex >= _records.Count) return;

            var rec = _records[_currentIndex];
            PageInfoLabel.Text = $"{_currentIndex + 1} / {_records.Count}";
            HeaderLabel.Text = $"Turn {rec.TurnNumber} | {rec.Direction}";
            
            PrevButton.IsEnabled = _currentIndex > 0;
            NextButton.IsEnabled = _currentIndex < _records.Count - 1;

            var doc = new FlowDocument();
            string text = rec.Text ?? "";
            
            if (!string.IsNullOrWhiteSpace(rec.Summary))
            {
                var summaryRun = new Run($"【要約】{rec.Summary}\n\n") { FontWeight = FontWeights.Bold };
                doc.Blocks.Add(new Paragraph(summaryRun));
            }

            foreach (var line in text.Split('\n'))
            {
                doc.Blocks.Add(new Paragraph(new Run(line)) { Margin = new Thickness(0, 0, 0, 8) });
            }

            FullTextBox.Document = doc;
        }

        private void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIndex > 0)
            {
                _currentIndex--;
                RenderCurrentRecord();
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIndex < _records.Count - 1)
            {
                _currentIndex++;
                RenderCurrentRecord();
            }
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_records.Count > 0)
            {
                Clipboard.SetText(_records[_currentIndex].Text ?? "");
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}