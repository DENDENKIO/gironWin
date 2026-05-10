namespace gironWin
{
    /// <summary>
    /// ターン順序ポリシー。
    /// </summary>
    public enum TurnPolicy
    {
        /// <summary>左右交互に発言する基本モード。</summary>
        RoundRobin,

        /// <summary>司会が発言者を選択する。</summary>
        ModeratorSelect,

        /// <summary>
        /// 人間優先モード。各 AI ターンの後、人間が割り込み入力を行える。
        /// タイムアウト（HumanPriorityTimeoutMs）内に入力がなければ自動で AI ターンへ移行する。
        /// </summary>
        HumanPriority,

        /// <summary>提案→批判→改善 の3フェーズサイクル。</summary>
        CritiqueThenRefine,

        /// <summary>仮説→証明→反例→査読 の4フェーズサイクル。</summary>
        ResearchReviewLoop
    }
}
