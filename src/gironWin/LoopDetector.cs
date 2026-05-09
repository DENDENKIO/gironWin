using System;
using System.Collections.Generic;

namespace gironWin
{
    /// <summary>
    /// 同一・類似メッセージの無限ループを検知する。
    /// 仕様書 Phase 2「ループ検知」対応。
    /// </summary>
    public sealed class LoopDetector
    {
        // ── 設定 ──────────────────────────────────────────
        /// <summary>直近何件を比較対象とするか</summary>
        public int WindowSize { get; set; } = 4;

        /// <summary>連続完全一致が何回続いたら検知とするか</summary>
        public int ExactRepeatThreshold { get; set; } = 2;

        /// <summary>類似度がこの値以上なら「類似」とみなす (0.0〜1.0)</summary>
        public double SimilarityThreshold { get; set; } = 0.92;

        /// <summary>類似が何回続いたら検知とするか</summary>
        public int SimilarRepeatThreshold { get; set; } = 3;
        // ──────────────────────────────────────────────────

        private readonly Queue<string> _window = new();

        /// <summary>テキストを追加し、ループを検知したら true を返す。</summary>
        public bool AddAndCheck(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            _window.Enqueue(text);
            while (_window.Count > WindowSize)
                _window.Dequeue();

            if (_window.Count < 2) return false;

            var items = new List<string>(_window);

            // ── 完全一致チェック ──
            int exactCount = 1;
            for (int i = items.Count - 2; i >= 0; i--)
            {
                if (items[i] == items[^1])
                    exactCount++;
                else
                    break;
            }
            if (exactCount >= ExactRepeatThreshold) return true;

            // ── 類似度チェック ──
            int similarCount = 1;
            for (int i = items.Count - 2; i >= 0; i--)
            {
                double sim = ComputeSimilarity(items[i], items[^1]);
                if (sim >= SimilarityThreshold)
                    similarCount++;
                else
                    break;
            }
            if (similarCount >= SimilarRepeatThreshold) return true;

            return false;
        }

        public void Reset() => _window.Clear();

        // ── Dice 係数（バイグラム）による類似度 ──
        private static double ComputeSimilarity(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;
            if (a == b) return 1.0;

            var setA = GetBigrams(a);
            var setB = GetBigrams(b);
            if (setA.Count == 0 || setB.Count == 0) return 0;

            int intersection = 0;
            foreach (var bg in setA)
            {
                if (setB.Contains(bg)) intersection++;
            }
            return 2.0 * intersection / (setA.Count + setB.Count);
        }

        private static HashSet<string> GetBigrams(string s)
        {
            var set = new HashSet<string>();
            // 比較コストを下げるため先頭500文字だけ使う
            int len = Math.Min(s.Length, 500);
            for (int i = 0; i + 1 < len; i++)
                set.Add(s.Substring(i, 2));
            return set;
        }
    }
}
