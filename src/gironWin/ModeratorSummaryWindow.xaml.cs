using System.Collections.Generic;
using System.Windows;

namespace gironWin
{
    public partial class ModeratorSummaryWindow : Window
    {
        public ModeratorSummaryWindow(IReadOnlyList<TransferRecord> records)
        {
            InitializeComponent();
            var summary = ModeratorSummaryBuilder.Build(records);

            IssuesBox.Text        = summary.Issues;
            AgreementsBox.Text    = summary.Agreements;
            DisagreementsBox.Text = summary.Disagreements;
            FullTextBox.Text      = summary.FullText;
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            string active = (SummaryTabControl.SelectedItem as System.Windows.Controls.TabItem)
                            ?.Header?.ToString() ?? "";
            string text = active switch
            {
                "論点整理" => IssuesBox.Text,
                "合意点"   => AgreementsBox.Text,
                "対立点"   => DisagreementsBox.Text,
                _          => FullTextBox.Text
            };
            Clipboard.SetText(text);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }

    // ---------------------------------------------------------------
    // サマリー構築ロジック（キーワードベース簡易実装）
    // ---------------------------------------------------------------
    public record ModeratorSummary(
        string Issues,
        string Agreements,
        string Disagreements,
        string FullText);

    public static class ModeratorSummaryBuilder
    {
        public static ModeratorSummary Build(IReadOnlyList<TransferRecord> records)
        {
            var issues        = new System.Text.StringBuilder();
            var agreements    = new System.Text.StringBuilder();
            var disagreements = new System.Text.StringBuilder();
            var full          = new System.Text.StringBuilder();

            foreach (var r in records)
            {
                string line = $"[Turn {r.TurnNumber} {r.Direction}] {r.Summary}";
                full.AppendLine(line);

                string t = r.Text.ToLowerInvariant();

                // 合意キーワード
                if (t.Contains("同意") || t.Contains("賛成") || t.Contains("その通り")
                 || t.Contains("agree") || t.Contains("correct"))
                    agreements.AppendLine(line);

                // 対立キーワード
                else if (t.Contains("しかし") || t.Contains("反論") || t.Contains("異議")
                      || t.Contains("however") || t.Contains("disagree") || t.Contains("反例"))
                    disagreements.AppendLine(line);

                // その他は論点整理
                else
                    issues.AppendLine(line);
            }

            return new ModeratorSummary(
                Issues:        issues.Length        > 0 ? issues.ToString()        : "（なし）",
                Agreements:    agreements.Length    > 0 ? agreements.ToString()    : "（なし）",
                Disagreements: disagreements.Length > 0 ? disagreements.ToString() : "（なし）",
                FullText:      full.Length          > 0 ? full.ToString()          : "（なし）"
            );
        }
    }
}
