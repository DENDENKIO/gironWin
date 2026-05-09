namespace gironWin
{
    /// <summary>
    /// FR-06 / Phase 4: ターン制御ポリシー
    /// </summary>
    public enum TurnPolicy
    {
        /// <summary>左→右→左… 固定往復</summary>
        RoundRobin,
        /// <summary>提案→批判→改善 の 3 フェーズサイクル</summary>
        CritiqueThenRefine,
        /// <summary>仮説→証明案→反例→査読 の 4 フェーズサイクル</summary>
        ResearchReviewLoop,
        /// <summary>第3席（司会）が都度次発言者を選択</summary>
        ModeratorSelect,
        /// <summary>人間介入要求を最優先</summary>
        HumanPriority
    }
}
