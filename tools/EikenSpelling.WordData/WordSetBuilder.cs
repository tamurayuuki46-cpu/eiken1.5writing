using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;

namespace EikenSpelling.WordData;

internal sealed record BuildOptions(string WordSetVersion, string SourceVersion, bool AllowMissingTranslations);

/// <summary>頭文字と文字数を添えても区別できない語の組(同じ意味・同じ頭文字・同じ長さ)。</summary>
internal sealed record AmbiguousGroup(string Meaning, IReadOnlyList<string> Words);

/// <summary>組み立ての結果と、レポートに出す内訳。Errors が空でなければ words.json を書かない。</summary>
internal sealed record BuildResult(
    WordSet Set,
    IReadOnlyList<RejectedWord> Rejected,
    IReadOnlyList<(string Word, string Reason)> Excluded,
    IReadOnlyList<string> MissingTranslations,
    IReadOnlyList<IReadOnlyList<string>> SameTranslationGroups,
    IReadOnlyDictionary<string, int> BlankRules,
    IReadOnlyList<string> UnknownCuratedWords,
    IReadOnlyList<string> Errors)
{
    /// <summary>意味が重なるので頭文字と文字数を添えた語の数。</summary>
    public int HintedCount { get; init; }

    public IReadOnlyList<AmbiguousGroup> AmbiguousGroups { get; init; } = [];
}

/// <summary>
/// 統合した語に付加データを重ねて単語データを作る(word-data design 4章)。
/// 除外(1〜2文字・除外の一覧)→ 訳 → Tier → 穴あき(手動優先、無ければ自動生成)
/// → 意味が重なる語に頭文字と文字数を添える(それでも区別できなければ誤り)→ 同じ訳は日本語モード不可 → 検証。
/// </summary>
internal static class WordSetBuilder
{
    /// <summary>これ以下の長さの語は除外する(requirements Q1。全部を隠さずに穴を作れない長さでもある)。</summary>
    public const int ExcludeUpToLength = 2;

    public static BuildResult Build(MergeResult merged, int ngslMaxRank, Curation curation, BuildOptions options)
    {
        var excluded = new List<(string, string)>();
        var missing = new List<string>();
        var errors = new List<string>();
        var blankRules = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var entries = new List<WordEntry>();
        var known = merged.Words.Select(w => w.Word).ToHashSet(StringComparer.Ordinal);

        foreach (var w in merged.Words)
        {
            if (w.Word.Length <= ExcludeUpToLength)
            {
                excluded.Add((w.Word, $"1〜{ExcludeUpToLength}文字"));
                continue;
            }

            if (curation.Excluded.TryGetValue(w.Word, out var reason))
            {
                excluded.Add((w.Word, reason));
                continue;
            }

            if (!curation.Translations.TryGetValue(w.Word, out var ja))
            {
                missing.Add(w.Word);
                continue; // 訳のない語は出さない(作業の途中でも生成できる。最終は AllowMissingTranslations = false で誤り)
            }

            string blanks;
            BlankSource blankSource;
            if (curation.ManualBlanks.TryGetValue(w.Word, out var manual))
            {
                blanks = manual;
                blankSource = BlankSource.Manual;
                Count(blankRules, "Manual");
            }
            else
            {
                var generated = BlankGenerator.Generate(w.Word);
                blanks = generated.Blanks;
                blankSource = BlankSource.Auto;
                Count(blankRules, generated.Rule.ToString());
            }

            entries.Add(new WordEntry
            {
                Id = w.Word,
                Word = w.Word,
                Ja = ja,
                Blanks = blanks,
                BlankSource = blankSource,
                Sources = w.Sources,
                PriorityTier = Tier(w, curation),
                FrequencyRank = WordMerger.FrequencyRank(w, ngslMaxRank),
            });
        }

        // 意味が重なる語(訳を「、」で区切り括弧を外した意味単位で、ほかの語と同じものがある)には頭文字と文字数を添える。
        // 日本語モードで答えを一つに決められるようにするため(仕様 v1.1 6.1、2026-11-14 ユーザー決定)
        var meaningsByWord = entries.ToDictionary(e => e.Word, e => Meanings(e.Ja), StringComparer.Ordinal);
        var wordsByMeaning = meaningsByWord
            .SelectMany(kv => kv.Value.Select(m => (Meaning: m, Word: kv.Key)))
            .GroupBy(x => x.Meaning, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Word).Distinct(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
        var hinted = meaningsByWord
            .Where(kv => kv.Value.Any(m => wordsByMeaning[m].Count > 1))
            .Select(kv => kv.Key)
            .ToHashSet(StringComparer.Ordinal);
        entries = [.. entries.Select(e => hinted.Contains(e.Word) ? e with { Ja = $"{e.Ja}{Hint(e.Word)}" } : e)];

        // 頭文字と文字数も同じで、同じ意味を持つ語は区別できない → 誤り(訳を書き分けて直す)
        var ambiguousGroups = wordsByMeaning
            .SelectMany(kv => kv.Value
                .GroupBy(Hint, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => new AmbiguousGroup(kv.Key, [.. g.Order(StringComparer.Ordinal)])))
            .OrderBy(g => g.Words[0], StringComparer.Ordinal)
            .ThenBy(g => g.Meaning, StringComparer.Ordinal)
            .ToList();
        errors.AddRange(ambiguousGroups.Select(g => $"頭文字と文字数を添えても区別できない「{g.Meaning}」: {string.Join(", ", g.Words)}"));

        // 同じ訳の語は日本語モード不可(仕様 v1.1 6.1)。訳を直して区別できれば使えるようになる
        var sameGroups = entries
            .GroupBy(e => e.Ja.Trim(), StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => (IReadOnlyList<string>)[.. g.Select(e => e.Word).Order(StringComparer.Ordinal)])
            .OrderBy(g => g[0], StringComparer.Ordinal)
            .ToList();
        var ambiguous = sameGroups.SelectMany(g => g).ToHashSet(StringComparer.Ordinal);
        entries = [.. entries.Select(e => ambiguous.Contains(e.Word) ? e with { JapaneseEligible = false } : e)];

        if (missing.Count > 0 && !options.AllowMissingTranslations)
        {
            errors.Add($"訳のない語が {missing.Count} 語あります。");
        }

        if (entries.Count == 0)
        {
            errors.Add("収録する語が 0 語です(訳がまだ無いなど)。今の words.json を空で上書きしないよう止めます。");
        }

        var unknown = curation.Translations.Keys
            .Concat(curation.Tier1)
            .Concat(curation.Excluded.Keys)
            .Concat(curation.ManualBlanks.Keys)
            .Where(word => !known.Contains(word))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (unknown.Count > 0)
        {
            errors.Add($"付加データに元リストに無い語が {unknown.Count} 語あります(書き間違いの可能性): {string.Join(", ", unknown)}");
        }

        var set = new WordSet { WordSetVersion = options.WordSetVersion, SourceVersion = options.SourceVersion, Words = entries };
        errors.AddRange(WordSetValidator.Validate(set).Where(i => i.Severity == ValidationSeverity.Error).Select(i => $"{i.WordId}: {i.Message}"));
        return new BuildResult(set, merged.Rejected, excluded, missing, sameGroups, blankRules, unknown, errors)
        {
            HintedCount = hinted.Count,
            AmbiguousGroups = ambiguousGroups,
        };
    }

    /// <summary>訳の意味単位: 「、」で区切り、括弧(全角・半角)の中を除いて前後の空白を取る。</summary>
    public static IReadOnlyList<string> Meanings(string ja) =>
        [.. ja.Split('、')
            .Select(part => System.Text.RegularExpressions.Regex.Replace(part, @"[(（][^)）]*[)）]", ""))
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .Distinct(StringComparer.Ordinal)];

    /// <summary>添える手がかり: 頭文字と文字数(例: ban → 「(b・3文字)」)。</summary>
    public static string Hint(string word) => $"({word[0]}・{word.Length}文字)";

    /// <summary>Tier(仕様 v1.1 14.2): Tier 1 の一覧 → 1、NAWL・BSL の語 → 2、それ以外 → 3。</summary>
    private static int Tier(MergedWord w, Curation curation) =>
        curation.Tier1.Contains(w.Word) ? 1
        : w.Sources.Contains(SourceLists.Nawl) || w.Sources.Contains(SourceLists.Bsl) ? 2
        : 3;

    private static void Count(SortedDictionary<string, int> counts, string key) =>
        counts[key] = counts.GetValueOrDefault(key) + 1;
}
