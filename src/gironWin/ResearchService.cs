using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace gironWin
{
    /// <summary>
    /// FR-13: テキストから研究タグを抽出・管理するサービス。
    /// </summary>
    public class ResearchService
    {
        public ObservableCollection<ResearchTagEntry> Entries { get; } = new();

        // タグキーワード → TagType のマッピング
        private static readonly Dictionary<string, string> _tagMap = new()
        {
            { "\u547d\u984c",     "Proposition"     },
            { "\u5b9a\u7fa9",     "Definition"      },
            { "\u4eee\u5b9a",     "Assumption"      },
            { "\u8a3c\u660e\u65b9\u91dd", "ProofIdea"       },
            { "\u88dc\u984c",     "LemmaCandidate"  },
            { "\u53cd\u4f8b",     "Counterexample"  },
            { "\u8ad6\u7406\u306e\u7a74", "Gap"             },
            { "\u672a\u691c\u8a3c",   "Unverified"      },
            { "\u5c0e\u51fa\u6e08\u307f", "Derived"         },
            // 英語キーワードにも対応
            { "proposition",  "Proposition"    },
            { "definition",   "Definition"     },
            { "assumption",   "Assumption"     },
            { "proof",        "ProofIdea"      },
            { "lemma",        "LemmaCandidate" },
            { "counterexample","Counterexample"},
            { "gap",          "Gap"            },
            { "unverified",   "Unverified"     },
        };

        public List<ResearchTagEntry> ExtractAndAdd(
            string text, int turnNumber, string messageId)
        {
            var added = new List<ResearchTagEntry>();
            if (string.IsNullOrWhiteSpace(text)) return added;

            // 各行をスキャンしてキーワードを探す
            var lines = text.Split('\n');
            foreach (var line in lines)
            {
                string lower = line.ToLower();
                foreach (var kv in _tagMap)
                {
                    if (lower.Contains(kv.Key.ToLower()))
                    {
                        var entry = new ResearchTagEntry
                        {
                            TagType    = kv.Value,
                            Content    = line.Trim(),
                            TurnNumber = turnNumber,
                            MessageId  = messageId
                        };
                        Entries.Add(entry);
                        added.Add(entry);
                        break; // 1行1タグ
                    }
                }
            }

            return added;
        }

        public void Clear() => Entries.Clear();
    }
}
