using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;

namespace EikenSpelling.WordData;

internal sealed record BuildOptions(string WordSetVersion, string SourceVersion, bool AllowMissingTranslations);

/// <summary>組み立ての結果と、レポートに出す内訳。Errors が空でなければ words.json を書かない。</summary>
internal sealed record BuildResult(
    WordSet Set,
    IReadOnlyList<RejectedWord> Rejected,
    IReadOnlyList<(string Word, string Reason)> Excluded,
    IReadOnlyList<string> MissingTranslations,
    IReadOnlyList<IReadOnlyList<string>> SameTranslationGroups,
    IReadOnlyDictionary<string, int> BlankRules,
    IReadOnlyList<string> UnknownCuratedWords,
    IReadOnlyList<string> Errors);

/// <summary>
/// 統合した語に付加データを重ねて単語データを作る(word-data design 4章)。
/// 除外(1〜2文字・除外の一覧)→ 訳 → Tier → 穴あき(手動優先、無ければ自動生成)→ 同じ訳は日本語モード不可 → 検証。
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
                excluded.Add((w.Word, "1〜2文字"));
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

        var set = new WordSet { WordSetVersion = options.WordSetVersion, SourceVersion = options.SourceVersion, Words = entries };
        errors.AddRange(WordSetValidator.Validate(set).Where(i => i.Severity == ValidationSeverity.Error).Select(i => $"{i.WordId}: {i.Message}"));
        return new BuildResult(set, merged.Rejected, excluded, missing, sameGroups, blankRules, unknown, errors);
    }

    /// <summary>Tier(仕様 v1.1 14.2): Tier 1 の一覧 → 1、NAWL・BSL の語 → 2、それ以外 → 3。</summary>
    private static int Tier(MergedWord w, Curation curation) =>
        curation.Tier1.Contains(w.Word) ? 1
        : w.Sources.Contains(SourceLists.Nawl) || w.Sources.Contains(SourceLists.Bsl) ? 2
        : 3;

    private static void Count(SortedDictionary<string, int> counts, string key) =>
        counts[key] = counts.GetValueOrDefault(key) + 1;
}
