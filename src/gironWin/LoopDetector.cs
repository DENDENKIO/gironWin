using System;
using System.Collections.Generic;

namespace gironWin
{
    /// <summary>
    /// 自動討論のループを検知する。<br/>
    /// ・完全一致: 直前と同じテキストが 2 回連続 → ループ<br/>
    /// ・類似度: Dice バイグラム係数が 92% 以上で 3 回連続 → ループ
    /// </summary>
    public class LoopDetector
    {
        private const int    SimilarWindowSize = 3;
        private const double SimilarThreshold  = 0.92;
        private const int    CompareLength     = 500;

        private string _prevText = string.Empty;
        private readonly Queue<string> _window = new();

        public void Reset()
        {
            _prevText = string.Empty;
            _window.Clear();
        }

        /// <summary>
        /// テキストを追加しループかどうかを判定する。true の場合ループ。
        /// </summary>
        public bool AddAndCheck(string text)
        {
            string snippet = Clip(text);

            // 完全一致チェック
            if (!string.IsNullOrWhiteSpace(_prevText) &&
                string.Equals(snippet, Clip(_prevText), StringComparison.Ordinal))
            {
                _prevText = text;
                return true;
            }

            _prevText = text;

            // 類似度ウィンドウ
            _window.Enqueue(snippet);
            if (_window.Count > SimilarWindowSize)
                _window.Dequeue();

            if (_window.Count == SimilarWindowSize)
            {
                var arr = _window.ToArray();
                bool allSimilar = true;
                for (int i = 1; i < arr.Length; i++)
                {
                    if (DiceBigram(arr[0], arr[i]) < SimilarThreshold)
                    {
                        allSimilar = false;
                        break;
                    }
                }
                if (allSimilar) return true;
            }

            return false;
        }

        private static string Clip(string s) =>
            s.Length <= CompareLength ? s : s.Substring(0, CompareLength);

        private static double DiceBigram(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;
            var setA = Bigrams(a);
            var setB = Bigrams(b);

            int intersection = 0;
            foreach (var bg in setA)
                if (setB.Contains(bg)) intersection++;

            return (2.0 * intersection) / (setA.Count + setB.Count);
        }

        private static HashSet<string> Bigrams(string s)
        {
            var set = new HashSet<string>();
            for (int i = 0; i < s.Length - 1; i++)
                set.Add(s.Substring(i, 2));
            return set;
        }
    }
}
