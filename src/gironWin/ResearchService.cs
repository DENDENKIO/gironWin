using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace gironWin
{
    /// <summary>
    /// Phase 5 強化版 ResearchService
    /// ・構造化タグ（Proposition / Definition / … / Rigor 等）
    /// ・サブタグ抽出（例: Counterexample / Specific）
    /// ・重要度スコアリング
    /// </summary>
    public sealed class ResearchModeService
    {
        private readonly List<ResearchTagEntry> _entries = new();
        public IReadOnlyList<ResearchTagEntry> Entries => _entries;

        // ---------------------------------------------------------------
        // 外部 API
        // ---------------------------------------------------------------

        /// <summary>テキストを解析してタグ登録し、新規タグリストを返す。</summary>
        public List<ResearchTagEntry> ExtractAndRegister(string text, int turn)
        {
            var newTags = new List<ResearchTagEntry>();
            if (string.IsNullOrWhiteSpace(text)) return newTags;

            foreach (var rule in Rules)
            {
                foreach (Match m in rule.Pattern.Matches(text))
                {
                    string snippet = Truncate(m.Value, 120);
                    int importance = ScoreImportance(text, rule.TagType);

                    var entry = new ResearchTagEntry
                    {
                        TagType     = rule.TagType,
                        SubTagType  = rule.SubTagType,
                        Text        = snippet,
                        TurnNumber  = turn,
                        MessageId   = $"msg-{turn}",
                        Importance  = importance
                    };
                    _entries.Add(entry);
                    newTags.Add(entry);
                }
            }
            return newTags;
        }

        // ---------------------------------------------------------------
        // ルール定義
        // ---------------------------------------------------------------

        private record TagRule(string TagType, string SubTagType, Regex Pattern);

        private static readonly List<TagRule> Rules = new()
        {
            // Proposition（命題）
            new(ResearchTagTypes.Proposition, "",
                new Regex(@"(?i)(命題|定理|主張|Theorem|Proposition|Claim)[^\n。]{0,80}",
                    RegexOptions.Compiled)),

            // Definition（定義）
            new(ResearchTagTypes.Definition, "",
                new Regex(@"(?i)(定義|define|definition)[^\n。]{0,80}",
                    RegexOptions.Compiled)),

            // Assumption（仮定）
            new(ResearchTagTypes.Assumption, "",
                new Regex(@"(?i)(仮定|assume|assumption|前提)[^\n。]{0,80}",
                    RegexOptions.Compiled)),

            // ProofIdea（証明方針）
            new(ResearchTagTypes.ProofIdea, "",
                new Regex(@"(?i)(証明方針|proof idea|示すには|帰納法|背理法|構成的証明)[^\n。]{0,80}",
                    RegexOptions.Compiled)),

            // LemmaCandidate（補題候補）
            new(ResearchTagTypes.LemmaCandidate, "",
                new Regex(@"(?i)(補題|lemma|系|corollary)[^\n。]{0,80}",
                    RegexOptions.Compiled)),

            // Counterexample / Specific
            new(ResearchTagTypes.Counterexample, "Specific",
                new Regex(@"(?i)(反例|counter.?example|具体的な反例)[^\n。]{0,80}",
                    RegexOptions.Compiled)),

            // Counterexample / Candidate
            new(ResearchTagTypes.Counterexample, "Candidate",
                new Regex(@"(?i)(反例候補|候補として|might be a counter)[^\n。]{0,80}",
                    RegexOptions.Compiled)),

            // Gap（論理の穴）
            new(ResearchTagTypes.Gap, "",
                new Regex(@"(?i)(論理の穴|gap|飛躍|不十分|未証明|証明されていない)[^\n。]{0,80}",
                    RegexOptions.Compiled)),

            // Unverified（未検証）
            new(ResearchTagTypes.Unverified, "",
                new Regex(@"(?i)(未検証|unverified|確認が必要|要検証)[^\n。]{0,80}",
                    RegexOptions.Compiled)),

            // Derived（導出済み）
            new(ResearchTagTypes.Derived, "",
                new Regex(@"(?i)(導出|derive|したがって.*証明|から得られる)[^\n。]{0,80}",
                    RegexOptions.Compiled)),

            // OpenQuestion（未解決問題）
            new(ResearchTagTypes.OpenQuestion, "",
                new Regex(@"(?i)(未解決|open question|未証明問題|open problem)[^\n。]{0,80}",
                    RegexOptions.Compiled)),

            // Rigor（厳密性）
            new(ResearchTagTypes.Rigor, "",
                new Regex(@"(?i)(厳密|rigor|形式的に|形式化|formally)[^\n。]{0,80}",
                    RegexOptions.Compiled)),

            // Agreement（合意）
            new(ResearchTagTypes.Agreement, "",
                new Regex(@"(?i)(同意|同感|その通り|賛成|agree|correct)[^\n。]{0,60}",
                    RegexOptions.Compiled)),

            // Disagreement（対立）
            new(ResearchTagTypes.Disagreement, "",
                new Regex(@"(?i)(異議|反論|しかし|disagree|however.*not)[^\n。]{0,80}",
                    RegexOptions.Compiled)),
        };

        // ---------------------------------------------------------------
        // 重要度スコアリング
        // ---------------------------------------------------------------

        private static int ScoreImportance(string text, string tagType)
        {
            // OpenQuestion / Gap / Counterexample は高重要度
            if (tagType is ResearchTagTypes.OpenQuestion
                        or ResearchTagTypes.Gap
                        or ResearchTagTypes.Counterexample)
                return 3;

            // Proposition / Derived は中
            if (tagType is ResearchTagTypes.Proposition
                        or ResearchTagTypes.Derived
                        or ResearchTagTypes.Rigor)
                return 2;

            return 1;
        }

        private static string Truncate(string s, int max)
            => s.Length <= max ? s : s[..max] + "…";
    }
}
