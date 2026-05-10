using System.Text;

namespace gironWin
{
    /// <summary>
    /// Phase 5 厳密性チェック支援プロンプト生成ヘルパー。
    /// 討論テキストに対して厳密性チェック指示を注入したプロンプトを生成する。
    /// </summary>
    public static class RigorPromptHelper
    {
        // ---------------------------------------------------------------
        // 公開 API
        // ---------------------------------------------------------------

        /// <summary>
        /// 汎用厳密性チェックプロンプトを生成する。
        /// </summary>
        public static string Build(RigorCheckMode mode, string targetText)
        {
            var sb = new StringBuilder();
            sb.AppendLine(GetModeInstruction(mode));
            sb.AppendLine();
            sb.AppendLine("【チェック対象テキスト】");
            sb.AppendLine(targetText);
            sb.AppendLine();
            sb.AppendLine(GetOutputFormat(mode));
            return sb.ToString();
        }

        /// <summary>
        /// 研究タグエントリーに基づく特化プロンプトを生成する。
        /// </summary>
        public static string BuildFromEntry(ResearchTagEntry entry)
        {
            var mode = entry.TagType switch
            {
                ResearchTagTypes.Counterexample => RigorCheckMode.Counterexample,
                ResearchTagTypes.Gap            => RigorCheckMode.GapAnalysis,
                ResearchTagTypes.ProofIdea      => RigorCheckMode.ProofVerify,
                ResearchTagTypes.OpenQuestion   => RigorCheckMode.OpenQuestion,
                _                               => RigorCheckMode.General
            };
            return Build(mode, entry.Text);
        }

        // ---------------------------------------------------------------
        // モード別指示文
        // ---------------------------------------------------------------

        private static string GetModeInstruction(RigorCheckMode mode) => mode switch
        {
            RigorCheckMode.ProofVerify =>
                """
                あなたは数学・論理の専門家として、以下の証明案を厳密に検証してください。
                各ステップが論理的に正当であるか、仮定が明示されているか、飛躍がないかを確認してください。
                """,

            RigorCheckMode.Counterexample =>
                """
                あなたは批判的思考の専門家として、以下の命題または主張に対する反例を探索してください。
                具体的な反例が存在する場合はその構成を示し、存在しない場合はその理由を述べてください。
                """,

            RigorCheckMode.GapAnalysis =>
                """
                あなたは論理分析の専門家として、以下の議論に含まれる論理的なギャップ・飛躍・未証明箇所を特定してください。
                特定した各ギャップに対して、補完するために必要な命題または補題を提案してください。
                """,

            RigorCheckMode.OpenQuestion =>
                """
                あなたは研究者として、以下の文脈から未解決の問題・未回答の問いを抽出してください。
                各問題の難易度・重要性・既知の部分的解答を整理してください。
                """,

            RigorCheckMode.FormalVerify =>
                """
                あなたは形式手法の専門家として、以下の主張を形式的に検証してください。
                型理論・集合論・述語論理のいずれか適切な形式体系を選択し、証明の概要を示してください。
                """,

            _ => // General
                """
                あなたは厳密性の専門家として、以下のテキストを多角的に検証してください。
                論理的一貫性・前提の妥当性・結論の正確性・見落としの有無を分析してください。
                """
        };

        private static string GetOutputFormat(RigorCheckMode mode) => mode switch
        {
            RigorCheckMode.ProofVerify =>
                """
                【出力形式】
                - 検証結果: 妥当 / 要修正 / 不十分
                - 問題点（箇条書き）
                - 修正提案
                """,

            RigorCheckMode.Counterexample =>
                """
                【出力形式】
                - 反例の有無: あり / なし
                - 反例（あれば具体的に）
                - 補足・考察
                """,

            RigorCheckMode.GapAnalysis =>
                """
                【出力形式】
                - ギャップ一覧（番号付き）
                - 各ギャップの重要度（高/中/低）
                - 補完提案
                """,

            RigorCheckMode.OpenQuestion =>
                """
                【出力形式】
                - 未解決問題一覧（番号付き）
                - 各問題の重要度と難易度
                - 関連する既知の結果
                """,

            _ =>
                """
                【出力形式】
                - 総合評価
                - 問題点（箇条書き）
                - 改善提案
                """
        };
    }

    // ---------------------------------------------------------------
    // モード列挙
    // ---------------------------------------------------------------
    public enum RigorCheckMode
    {
        General,         // 汎用厳密性チェック
        ProofVerify,     // 証明検証
        Counterexample,  // 反例探索
        GapAnalysis,     // ギャップ分析
        OpenQuestion,    // 未解決問題抽出
        FormalVerify     // 形式的検証
    }
}
