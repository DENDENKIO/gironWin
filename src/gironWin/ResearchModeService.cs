using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace gironWin
{
    /// <summary>
    /// FR-13 Phase 5: 数学・研究モード
    /// テキストから構造化タグを抽出し、未検証点一覧・研究ノートを生成する。
    /// </summary>
    public sealed class ResearchModeService
    {
        private readonly List<ResearchTagEntry> _entries = new();

        public IReadOnlyList<ResearchTagEntry> Entries => _entries;

        // ---------------------------------------------------------------
        // タグ抽出
        // ---------------------------------------------------------------

        /// <summary>
        /// テキストから研究タグを自動抽出して登録する。
        /// パターン例: 「命題: Fermat の最終定理は...」
        /// </summary>
        public List<ResearchTagEntry> ExtractAndRegister(string text, int turnNumber)
        {
            var found = new List<ResearchTagEntry>();

            foreach (ResearchTagType tag in System.Enum.GetValues(typeof(ResearchTagType)))
            {
                string label = GetDescription(tag);
                // パターン: 「ラベル:」または「ラベル：」に続く行
                var pattern = $@"(?:{label}|{tag})[:\uff1a]\s*(.+?)(?=\n|$)";
                var matches = Regex.Matches(text, pattern,
                    RegexOptions.IgnoreCase | RegexOptions.Multiline);

                foreach (Match m in matches)
                {
                    var entry = new ResearchTagEntry
                    {
                        TagType    = tag,
                        Label      = $"Turn {turnNumber}",
                        Content    = m.Groups[1].Value.Trim(),
                        IsVerified = tag == ResearchTagType.Derived
                    };
                    _entries.Add(entry);
                    found.Add(entry);
                }
            }

            return found;
        }

        /// <summary>手動でタグを登録する。</summary>
        public void Register(ResearchTagEntry entry) => _entries.Add(entry);

        /// <summary>IsVerified フラグを切り替える。</summary>
        public void ToggleVerified(ResearchTagEntry entry) => entry.IsVerified = !entry.IsVerified;

        // ---------------------------------------------------------------
        // 未検証点一覧
        // ---------------------------------------------------------------

        public IEnumerable<ResearchTagEntry> GetUnverified() =>
            _entries.Where(e => !e.IsVerified);

        public IEnumerable<ResearchTagEntry> GetByTag(ResearchTagType tag) =>
            _entries.Where(e => e.TagType == tag);

        // ---------------------------------------------------------------
        // 研究ノート出力（FR-14 成果物生成）
        // ---------------------------------------------------------------

        public string BuildResearchNote()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 研究ノート");
            sb.AppendLine();

            foreach (ResearchTagType tag in System.Enum.GetValues(typeof(ResearchTagType)))
            {
                var items = GetByTag(tag).ToList();
                if (items.Count == 0) continue;

                sb.AppendLine($"## {GetDescription(tag)}");
                foreach (var e in items)
                    sb.AppendLine($"- [{e.Label}] {e.Content}{(e.IsVerified ? " ✓" : " ❓")}");
                sb.AppendLine();
            }

            var unverified = GetUnverified().ToList();
            if (unverified.Count > 0)
            {
                sb.AppendLine("## ⚠ 未検証・要確認");
                foreach (var e in unverified)
                    sb.AppendLine($"- [{e.TagType}] {e.Label}: {e.Content}");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string GetDescription(ResearchTagType tag)
        {
            var fi = typeof(ResearchTagType).GetField(tag.ToString());
            return fi?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? tag.ToString();
        }
    }
}
