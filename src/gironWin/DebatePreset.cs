namespace gironWin
{
    /// <summary>
    /// FR-06 Phase 4: 討論プリセット
    /// </summary>
    public sealed class DebatePreset
    {
        public string     Name             { get; init; } = string.Empty;
        public TurnPolicy TurnPolicy       { get; init; } = TurnPolicy.RoundRobin;
        public DebateRole LeftRole         { get; init; } = DebateRole.None;
        public DebateRole RightRole        { get; init; } = DebateRole.None;
        public DebateRole ThirdRole        { get; init; } = DebateRole.Moderator;
        public string     LeftPrompt       { get; init; } = string.Empty;
        public string     RightPrompt      { get; init; } = string.Empty;
        public string     ThirdPrompt      { get; init; } = string.Empty;
        public bool       RequireApproval  { get; init; } = true;
        public bool       ResearchMode     { get; init; }
    }

    /// <summary>
    /// 組み込みプリセット一覧
    /// </summary>
    public static class BuiltInPresets
    {
        public static readonly DebatePreset ConstructiveDebate = new()
        {
            Name           = "建設的討論",
            TurnPolicy     = TurnPolicy.CritiqueThenRefine,
            LeftRole       = DebateRole.Proposer,
            RightRole      = DebateRole.Critic,
            ThirdRole      = DebateRole.Moderator,
            LeftPrompt     = "あなたは提案役です。具体的で建設的な提案を行ってください。",
            RightPrompt    = "あなたは批判役です。提案の弱点・リスク・代替案を指摘してください。",
            ThirdPrompt    = "あなたは司会です。論点を整理し、合意点と対立点をまとめ、次の問いを提示してください。",
            RequireApproval = false
        };

        public static readonly DebatePreset AppDesignReview = new()
        {
            Name           = "アプリ設計レビュー",
            TurnPolicy     = TurnPolicy.CritiqueThenRefine,
            LeftRole       = DebateRole.Proposer,
            RightRole      = DebateRole.Reviewer,
            ThirdRole      = DebateRole.Moderator,
            LeftPrompt     = "あなたは実装担当 AI です。要件・仕様・コードの設計案を提示してください。",
            RightPrompt    = "あなたはアーキテクト兼リスク指摘 AI です。設計のリスク・改善点・代替案を指摘してください。",
            ThirdPrompt    = "あなたは PO です。重要仕様とコード提案は承認制で確認し、議論を整理してください。",
            RequireApproval = true
        };

        public static readonly DebatePreset MathResearch = new()
        {
            Name           = "数学研究",
            TurnPolicy     = TurnPolicy.ResearchReviewLoop,
            LeftRole       = DebateRole.Proposer,
            RightRole      = DebateRole.Critic,
            ThirdRole      = DebateRole.Reviewer,
            LeftPrompt     = "あなたは証明案 AI です。命題の証明方針・補題候補を提示してください。必ず '命題:' '証明方針:' '補題候補:' の形式で明示してください。",
            RightPrompt    = "あなたは反例探索 AI です。証明の穴・反例候補・未検証点を指摘してください。必ず '反例候補:' '論理の穴:' '未検証:' の形式で明示してください。",
            ThirdPrompt    = "あなたは査読者です。どこが厳密に確定し、どこが未証明かを毎ターン整理し、検証状態を '導出済み:' '未検証:' で明示してください。",
            RequireApproval = true,
            ResearchMode   = true
        };

        public static DebatePreset[] All =>
            new[] { ConstructiveDebate, AppDesignReview, MathResearch };
    }
}
