using System.Collections.Generic;

namespace gironWin
{
    /// <summary>
    /// FR-06: 左席・右席のロール名・システムプロンプト・議題を保持する設定モデル。
    /// 旧 API（Presets リスト）との後方互換も維持。
    /// </summary>
    public sealed class PromptProfile
    {
        public string ProfileId         { get; set; } = "custom";
        public string DisplayName       { get; set; } = string.Empty;
        public string LeftName          { get; set; } = "\u5de6\u5e2dAI";
        public string RightName         { get; set; } = "\u53f3\u5e2dAI";
        public string LeftSystemPrompt  { get; set; } = string.Empty;
        public string RightSystemPrompt { get; set; } = string.Empty;
        public string Topic             { get; set; } = string.Empty;

        /// <summary>旧 API 互換: 単一プロンプト（左席プロンプトのエイリアス）</summary>
        public string SystemPrompt
        {
            get => LeftSystemPrompt;
            set => LeftSystemPrompt = value;
        }

        /// <summary>旧 API 互換: 役割ラベル</summary>
        public string Role { get; set; } = "Debater";

        public override string ToString() =>
            string.IsNullOrWhiteSpace(DisplayName) ? ProfileId : DisplayName;

        // ---------------------------------------------------------------
        // プリセット（新 API）
        // ---------------------------------------------------------------

        public static PromptProfile Default => new()
        {
            ProfileId         = "default",
            DisplayName       = "\u306a\u3057\uff08\u30d7\u30ec\u30fc\u30f3\uff09",
            LeftName          = "\u5de6\u5e2dAI",
            RightName         = "\u53f3\u5e2dAI",
            LeftSystemPrompt  = string.Empty,
            RightSystemPrompt = string.Empty,
            Topic             = string.Empty,
            Role              = "Debater"
        };

        public static PromptProfile DebatePreset => new()
        {
            ProfileId         = "debate",
            DisplayName       = "\u8a0e\u8ad6\uff08\u63d0\u6848 vs \u6279\u5224\uff09",
            LeftName          = "\u63d0\u6848\u8005",
            RightName         = "\u6279\u5224\u8005",
            LeftSystemPrompt  =
                "\u3042\u306a\u305f\u306f\u8b70\u984c\u306b\u5bfe\u3057\u3066\u7a4d\u6975\u7684\u306b\u63d0\u6848\u30fb\u4e3b\u5f35\u3092\u884c\u3046\u8a0e\u8ad6\u8005\u3067\u3059\u3002" +
                "\u8ad6\u7406\u7684\u304b\u3064\u5177\u4f53\u7684\u306b\u81ea\u5206\u306e\u7acb\u5834\u3092\u5c55\u958b\u3057\u3066\u304f\u3060\u3055\u3044\u3002",
            RightSystemPrompt =
                "\u3042\u306a\u305f\u306f\u76f8\u624b\u306e\u4e3b\u5f35\u3092\u6279\u5224\u7684\u306b\u691c\u8a0e\u3059\u308b\u8a0e\u8ad6\u8005\u3067\u3059\u3002" +
                "\u8ad6\u7406\u306e\u7a74\u3084\u8a3c\u62e0\u306e\u4e0d\u8db3\u3092\u6307\u6458\u3057\u3001\u4ee3\u66ff\u6848\u3092\u793a\u3057\u3066\u304f\u3060\u3055\u3044\u3002",
            Topic = string.Empty,
            Role  = "Debater"
        };

        public static PromptProfile ResearchPreset => new()
        {
            ProfileId         = "research",
            DisplayName       = "\u7814\u7a76\uff08\u4eee\u8aac vs \u8a3c\u660e\uff09",
            LeftName          = "\u4eee\u8aac\u63d0\u5531\u8005",
            RightName         = "\u8a3c\u660e\u30fb\u53cd\u4f8b\u691c\u8a0e\u8005",
            LeftSystemPrompt  =
                "\u3042\u306a\u305f\u306f\u6570\u5b66\u30fb\u79d1\u5b66\u7684\u306a\u4eee\u8aac\u3092\u63d0\u5531\u3059\u308b\u7814\u7a76\u8005\u3067\u3059\u3002" +
                "\u4eee\u8aac\u3092\u660e\u78ba\u306b\u8ff0\u3079\u3001\u76f4\u89b3\u7684\u6839\u62e0\u3092\u793a\u3057\u3066\u304f\u3060\u3055\u3044\u3002",
            RightSystemPrompt =
                "\u3042\u306a\u305f\u306f\u4eee\u8aac\u306e\u8a3c\u660e\u307e\u305f\u306f\u53cd\u4f8b\u3092\u691c\u8a0e\u3059\u308b\u6570\u5b66\u8005\u3067\u3059\u3002" +
                "\u53b3\u5bc6\u306a\u8ad6\u7406\u3068\u53cd\u4f8b\u63a2\u7d22\u3067\u4eee\u8aac\u306e\u59a5\u5f53\u6027\u3092\u8a55\u4fa1\u3057\u3066\u304f\u3060\u3055\u3044\u3002",
            Topic = string.Empty,
            Role  = "Researcher"
        };

        public static PromptProfile CritiquePreset => new()
        {
            ProfileId         = "critique",
            DisplayName       = "\u67fb\u8aad\uff08\u8457\u8005 vs \u67fb\u8aad\u8005\uff09",
            LeftName          = "\u8457\u8005",
            RightName         = "\u67fb\u8aad\u8005",
            LeftSystemPrompt  =
                "\u3042\u306a\u305f\u306f\u8ad6\u6587\u30fb\u6587\u66f8\u306e\u8457\u8005\u3067\u3059\u3002" +
                "\u81ea\u5206\u306e\u4e3b\u5f35\u3092\u4e01\u5be7\u306b\u8aac\u660e\u3057\u3001\u67fb\u8aad\u8005\u306e\u6307\u6458\u306b\u8aa0\u5b9f\u306b\u5fdc\u7b54\u3057\u3066\u304f\u3060\u3055\u3044\u3002",
            RightSystemPrompt =
                "\u3042\u306a\u305f\u306f\u53b3\u683c\u306a\u67fb\u8aad\u8005\u3067\u3059\u3002" +
                "\u8ad6\u7406\u30fb\u8a3c\u62e0\u30fb\u8a18\u8ff0\u306e\u660e\u78ba\u3055\u306e\u89b3\u70b9\u304b\u3089\u5efa\u8a2d\u7684\u306a\u6279\u5224\u3092\u884c\u3063\u3066\u304f\u3060\u3055\u3044\u3002",
            Topic = string.Empty,
            Role  = "Reviewer"
        };

        public static PromptProfile UltimateExpertVsBeginnerPreset => new()
        {
            ProfileId   = "ultimate_expert_vs_beginner",
            DisplayName = "究極の専門家 vs たとえ上手な素人",
            LeftName    = "究極の専門家",
            RightName   = "たとえ上手な素人",
            LeftSystemPrompt =
                "あなたは対象分野について究極レベルの専門知識を持つ人物です。" +
                "厳密で体系的、正確で誤解のない説明を行ってください。" +
                "前提条件、定義、例外、限界、実務上の注意点も必要に応じて示してください。",
            RightSystemPrompt =
                "あなたは専門知識を一切持たない素人ですが、たとえ話や日常的な比喩で理解しようとするのが得意です。" +
                "わからないことは率直に質問し、専門家の説明を一般人向けのたとえで言い換えて確認してください。" +
                "知ったかぶりはせず、素朴だが本質的な疑問を投げかけてください。",
            Topic = string.Empty,
            Role  = "Dialogue"
        };

        // ---------------------------------------------------------------
        // 旧 API 互換: Presets リスト
        // ---------------------------------------------------------------
        public static IReadOnlyList<PromptProfile> Presets { get; } = new List<PromptProfile>
        {
            new()
            {
                ProfileId    = "none",
                DisplayName  = "\u306a\u3057\uff08\u30d7\u30ec\u30fc\u30f3\uff09",
                SystemPrompt = "",
                Role         = "Debater"
            },
            new()
            {
                ProfileId    = "debater",
                DisplayName  = "\u8ad6\u8005\uff08\u63d0\u6848\uff09",
                SystemPrompt = "\u3042\u306a\u305f\u306f\u5efa\u8a3a\u7684\u306a\u8ad6\u8005\u3067\u3059\u3002\u76f8\u624b\u306e\u610f\u898b\u3092\u8003\u616e\u3057\u306a\u304c\u3089\u3001\u5177\u4f53\u7684\u306a\u63d0\u6848\u3084\u6539\u5584\u6848\u3092\u793a\u3057\u3066\u304f\u3060\u3055\u3044\u3002",
                Role         = "Debater"
            },
            new()
            {
                ProfileId    = "critic",
                DisplayName  = "\u6279\u5224\u8005",
                SystemPrompt = "\u3042\u306a\u305f\u306f\u6279\u5224\u7684\u306a\u8ad6\u8005\u3067\u3059\u3002\u76f8\u624b\u306e\u8ad6\u70b9\u306e\u5f31\u70b9\u3001\u77db\u76fe\u3001\u8aa4\u8b2c\u3092\u6307\u6458\u3057\u3066\u304f\u3060\u3055\u3044\u3002",
                Role         = "Critic"
            },
            new()
            {
                ProfileId    = "moderator",
                DisplayName  = "\u53f8\u4f1a\u8005",
                SystemPrompt = "\u3042\u306a\u305f\u306f\u8ad6\u8b70\u306e\u53f8\u4f1a\u8005\u3067\u3059\u3002\u5404\u30bf\u30fc\u30f3\u306e\u5f8c\u306b\u8ad6\u70b9\u3092\u6574\u7406\u3057\u3001\u5408\u610f\u70b9\u30fb\u5bfe\u7acb\u70b9\u30fb\u6b21\u306e\u554f\u3044\u3092\u7c21\u6f54\u306b\u307e\u3068\u3081\u3066\u304f\u3060\u3055\u3044\u3002",
                Role         = "Moderator"
            },
            new()
            {
                ProfileId    = "refiner",
                DisplayName  = "\u6539\u5584\u8005",
                SystemPrompt = "\u3042\u306a\u305f\u306f\u6539\u5584\u8005\u3067\u3059\u3002\u76f8\u624b\u306e\u8ad6\u70b9\u3092\u53d7\u3051\u5165\u308c\u3001\u3055\u3089\u306b\u5177\u4f53\u7684\u3067\u5b9f\u73fe\u53ef\u80fd\u306a\u6539\u5584\u6848\u3092\u63d0\u793a\u3057\u3066\u304f\u3060\u3055\u3044\u3002",
                Role         = "Refiner"
            },
            new()
            {
                ProfileId    = "reviewer",
                DisplayName  = "\u67fb\u8aad\u8005",
                SystemPrompt = "\u3042\u306a\u305f\u306f\u67fb\u8aad\u8005\u3067\u3059\u3002\u6587\u7ae0\u30fb\u8a2d\u8a08\u30fb\u8ad6\u8b70\u306e\u54c1\u8cea\u3092\u8a55\u4fa1\u3057\u3001\u5177\u4f53\u7684\u306a\u6539\u5584\u70b9\u3068\u8a55\u4fa1\u6839\u62e0\u3092\u793a\u3057\u3066\u304f\u3060\u3055\u3044\u3002",
                Role         = "Reviewer"
            },
            new()
            {
                ProfileId    = "researcher",
                DisplayName  = "\u7814\u7a76\u8005\uff08\u6570\u5b66\uff09",
                SystemPrompt = "\u3042\u306a\u305f\u306f\u6570\u5b66\u7814\u7a76\u8005\u3067\u3059\u3002\u547d\u984c\u30fb\u5b9a\u7fa9\u30fb\u4eee\u5b9a\u30fb\u8a3c\u660e\u6848\u30fb\u53cd\u4f8b\u5019\u88dc\u30fb\u672a\u8a3c\u660e\u70b9\u3092\u5c3b\u6e21\u3057\u306a\u304f\u8ad6\u3058\u3066\u304f\u3060\u3055\u3044\u3002",
                Role         = "Researcher"
            }
        };
    }
}
