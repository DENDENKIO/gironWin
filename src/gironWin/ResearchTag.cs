using System.ComponentModel;

namespace gironWin
{
    /// <summary>
    /// FR-13 Phase 5: 数学・研究モードの構造化タグ
    /// </summary>
    public enum ResearchTagType
    {
        [Description("命題")] Proposition,
        [Description("定義")] Definition,
        [Description("仮定")] Assumption,
        [Description("証明方針")] ProofIdea,
        [Description("補題候補")] LemmaCandidate,
        [Description("反例候補")] Counterexample,
        [Description("論理の穴")] Gap,
        [Description("未検証")] Unverified,
        [Description("導出済み")] Derived
    }

    /// <summary>
    /// 1つの発言に付与される研究タグのエントリ
    /// </summary>
    public sealed class ResearchTagEntry
    {
        public ResearchTagType TagType   { get; set; }
        public string          Label     { get; set; } = string.Empty;
        public string          Content   { get; set; } = string.Empty;
        public bool            IsVerified { get; set; }

        public override string ToString() =>
            $"[{TagType}] {Label}: {Content}{(IsVerified ? " ✓" : " ?")}"
            .Trim();
    }
}
