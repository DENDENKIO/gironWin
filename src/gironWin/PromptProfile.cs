using System.Collections.Generic;

namespace gironWin
{
    /// <summary>
    /// 役割プロンプトプロファイル。
    /// 各AI席に設定し、送信文の先頭に自動付加する。
    /// </summary>
    public sealed class PromptProfile
    {
        public string ProfileId   { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        /// <summary>送信文先頭に付加するインストラクション・テキスト</summary>
        public string SystemPrompt { get; set; } = string.Empty;
        /// <summary>役割 (Debater / Critic / Moderator / Refiner / Reviewer / Researcher)</summary>
        public string Role         { get; set; } = "Debater";

        public override string ToString() => string.IsNullOrWhiteSpace(DisplayName) ? ProfileId : DisplayName;

        // プリセット
        public static IReadOnlyList<PromptProfile> Presets { get; } = new List<PromptProfile>
        {
            new()
            {
                ProfileId    = "none",
                DisplayName  = "なし（プレーン）",
                SystemPrompt = "",
                Role         = "Debater"
            },
            new()
            {
                ProfileId    = "debater",
                DisplayName  = "論者（提案）",
                SystemPrompt = "あなたは建診的な論者です。相手の意見を考慮しながら、具体的な提案や改善案を示してください。",
                Role         = "Debater"
            },
            new()
            {
                ProfileId    = "critic",
                DisplayName  = "批判者",
                SystemPrompt = "あなたは批判的な論者です。相手の論点の弱点、矛盾、誘&#12483;該を指摘してください。",
                Role         = "Critic"
            },
            new()
            {
                ProfileId    = "moderator",
                DisplayName  = "司会者",
                SystemPrompt = "あなたは論議の司会者です。各ターンの後に論点を整理し、合意点・対立点・次の問いを簡潔にまとめてください。",
                Role         = "Moderator"
            },
            new()
            {
                ProfileId    = "refiner",
                DisplayName  = "改善者",
                SystemPrompt = "あなたは改善者です。相手の論点を受け入れ、さらに具体的で実現可能な改善案を提示してください。",
                Role         = "Refiner"
            },
            new()
            {
                ProfileId    = "reviewer",
                DisplayName  = "活読者（査読）",
                SystemPrompt = "あなたは査読者です。文章・設計・論議の品質を評価し、具体的な改善点と評価根拠を示してください。",
                Role         = "Reviewer"
            },
            new()
            {
                ProfileId    = "researcher",
                DisplayName  = "研究者（数学）",
                SystemPrompt = "あなたは数学研究者です。命題・定義・仮定・証明案・反例候補・未証明点を屋渡しなく論じてください。",
                Role         = "Researcher"
            }
        };
    }
}
