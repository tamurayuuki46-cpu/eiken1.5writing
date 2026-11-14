namespace EikenSpelling.WordData;

/// <summary>統合後の1語。Sources は NGSL → NAWL → BSL の順で重複なし。</summary>
internal sealed record MergedWord(string Word, IReadOnlyList<string> Sources, int? NgslRank, int? BslRank);

/// <summary>統合しなかった語と理由(レポート用)。</summary>
internal sealed record RejectedWord(string Word, string Source, string Reason);

internal sealed record MergeResult(IReadOnlyList<MergedWord> Words, IReadOnlyList<RejectedWord> Rejected);

/// <summary>
/// 3リストの統合(word-data design 4.1)。小文字・前後の空白を除いて同じ綴りを1件にまとめる。
/// 英字以外(ハイフン・空白・アポストロフィ・数字)を含む語は統合せず理由を返す(正誤判定が途中の空白などを区別するため)。
/// </summary>
internal static class WordMerger
{
    public static MergeResult Merge(
        IReadOnlyList<SourceWord> ngsl, IReadOnlyList<SourceWord> nawl, IReadOnlyList<SourceWord> bsl)
    {
        var byWord = new Dictionary<string, (List<string> Sources, int? Ngsl, int? Bsl)>(StringComparer.Ordinal);
        var rejected = new List<RejectedWord>();

        void Add(IReadOnlyList<SourceWord> list, string source)
        {
            foreach (var item in list)
            {
                var word = item.Word.Trim().ToLowerInvariant();
                if (word.Length == 0 || !word.All(char.IsAsciiLetterLower))
                {
                    rejected.Add(new RejectedWord(item.Word, source, "英字以外を含む"));
                    continue;
                }

                if (!byWord.TryGetValue(word, out var entry))
                {
                    entry = ([], null, null);
                }

                if (!entry.Sources.Contains(source))
                {
                    entry.Sources.Add(source);
                }

                // 同じリストに同じ語が2回あれば、小さい(上位の)順位を使う
                if (source == SourceLists.Ngsl && item.Rank is int n)
                {
                    entry.Ngsl = entry.Ngsl is int old ? Math.Min(old, n) : n;
                }
                else if (source == SourceLists.Bsl && item.Rank is int b)
                {
                    entry.Bsl = entry.Bsl is int old ? Math.Min(old, b) : b;
                }

                byWord[word] = entry;
            }
        }

        Add(ngsl, SourceLists.Ngsl);
        Add(nawl, SourceLists.Nawl);
        Add(bsl, SourceLists.Bsl);

        var words = byWord
            .Select(kv => new MergedWord(kv.Key, kv.Value.Sources, kv.Value.Ngsl, kv.Value.Bsl))
            .OrderBy(w => w.Word, StringComparer.Ordinal)
            .ToList();
        return new MergeResult(words, rejected);
    }

    /// <summary>
    /// 頻度順位(design 4.2): NGSL の順位。NGSL に無く BSL にある語は「NGSL の最大順位 + BSL の順位」。
    /// どちらの順位も無い語(NAWL だけ・NGSL の補足だけ)は null(Tier の中で最後・同順位は乱数)。
    /// </summary>
    public static int? FrequencyRank(MergedWord word, int ngslMaxRank) =>
        word.NgslRank ?? (word.BslRank is int b ? ngslMaxRank + b : null);
}
