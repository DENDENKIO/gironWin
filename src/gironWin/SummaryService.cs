using System.Linq;
using System.Text;

namespace gironWin
{
    /// <summary>
    /// FR-12 Phase 3: 発言の1行要約・論点整理・司会サマリーを生成する。
    /// 現バージョンはローカルテキスト処理で実装。
    /// 将来的に AI API 連携で高品質化できる。
    /// </summary>
    public sealed class SummaryService
    {
        private const int OneLinerMaxLength = 80;

        // ---------------------------------------------------------------
        // 1行要約
        // ---------------------------------------------------------------

        /// <summary>テキストから1行要約を生成する。</summary>
        public string Summarize(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText)) return string.Empty;

            // 最初の句点または改行までを取得
            var firstLine = rawText
                .Split('\n')
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0) ?? rawText.Trim();

            // 句点・。で区切り最初の文
            int dotIdx = firstLine.IndexOf('。');
            if (dotIdx > 0 && dotIdx < OneLinerMaxLength)
                return firstLine[..(dotIdx + 1)];

            // 超長い場合は切り捨て
            return firstLine.Length > OneLinerMaxLength
                ? firstLine[..OneLinerMaxLength] + "…"
                : firstLine;
        }

        // ---------------------------------------------------------------
        // 司会サマリー（Phase 4 FR-12）
        // ---------------------------------------------------------------

        /// <summary>
        /// 複数ターンの TransferRecord から論点整理サマリーを生成する。
        /// </summary>
        public string BuildModeratorSummary(System.Collections.Generic.IEnumerable<TransferRecord> records)
        {
            var list = records.ToList();
            if (list.Count == 0) return "（発言なし）";

            var sb = new StringBuilder();
            sb.AppendLine("## 司会サマリー");
            sb.AppendLine();

            // ターン一覧
            sb.AppendLine("### 発言一覧");
            foreach (var r in list)
            {
                string summary = string.IsNullOrWhiteSpace(r.Summary)
                    ? Summarize(r.Text)
                    : r.Summary;
                sb.AppendLine($"- Turn {r.TurnNumber} [{r.Direction}]: {summary}");
            }
            sb.AppendLine();

            // 合意点・対立点の簡易抽出（キーワードベース）
            var agreed  = list.Where(r => ContainsAgreement(r.Text)).ToList();
            var opposed = list.Where(r => ContainsOpposition(r.Text)).ToList();

            if (agreed.Count > 0)
            {
                sb.AppendLine("### 合意点候補");
                foreach (var r in agreed)
                    sb.AppendLine($"  - Turn {r.TurnNumber}: {Summarize(r.Text)}");
                sb.AppendLine();
            }

            if (opposed.Count > 0)
            {
                sb.AppendLine("### 対立点候補");
                foreach (var r in opposed)
                    sb.AppendLine($"  - Turn {r.TurnNumber}: {Summarize(r.Text)}");
                sb.AppendLine();
            }

            sb.AppendLine("### 次の問い");
            sb.AppendLine("（ここにユーザーが論点を追記してください）");

            return sb.ToString();
        }

        private static bool ContainsAgreement(string text) =>
            text.Contains("同意") || text.Contains("賛成") || text.Contains("その通り") ||
            text.Contains("agree", System.StringComparison.OrdinalIgnoreCase) ||
            text.Contains("correct", System.StringComparison.OrdinalIgnoreCase);

        private static bool ContainsOpposition(string text) =>
            text.Contains("反論") || text.Contains("しかし") || text.Contains("一方") ||
            text.Contains("disagree", System.StringComparison.OrdinalIgnoreCase) ||
            text.Contains("however", System.StringComparison.OrdinalIgnoreCase);
    }
}
