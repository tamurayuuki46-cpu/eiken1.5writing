using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;

namespace EikenSpelling.WordData;

/// <summary>words.json と検証レポートを書く。words.json は1語1行(差分を読みやすくするため)。</summary>
internal static class Output
{
    /// <summary>Tier 1 の語数の目安(仕様 v1.1 14.3)。外れたらレポートで注意する。</summary>
    public const int Tier1Min = 300;
    public const int Tier1Max = 800;

    private static readonly JsonSerializerOptions EntryOptions = new(WordSetLoader.JsonOptions)
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All), // 日本語をそのまま書く(\u エスケープにしない)
    };

    public static string WordsJson(WordSet set)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append(CultureInfo.InvariantCulture, $"  \"schemaVersion\": {set.SchemaVersion},\n");
        sb.Append("  \"wordSetVersion\": ").Append(JsonSerializer.Serialize(set.WordSetVersion, EntryOptions)).Append(",\n");
        sb.Append("  \"sourceVersion\": ").Append(JsonSerializer.Serialize(set.SourceVersion, EntryOptions)).Append(",\n");
        sb.Append("  \"words\": [\n");
        for (var i = 0; i < set.Words.Count; i++)
        {
            sb.Append("    ").Append(JsonSerializer.Serialize(set.Words[i], EntryOptions));
            sb.Append(i + 1 < set.Words.Count ? ",\n" : "\n");
        }

        sb.Append("  ]\n}\n");
        return sb.ToString();
    }

    public static string Report(BuildResult r, IReadOnlyDictionary<string, int> sourceCounts, int mergedCount)
    {
        var sb = new StringBuilder();
        void Line(string text = "") => sb.Append(text).Append('\n');

        Line("# 単語データの検証レポート");
        Line();
        Line("道具 `tools/EikenSpelling.WordData` が自動で書く(手で直さない)。出典は `data/sources/SOURCES.md`。");
        Line();
        Line("## 語数");
        foreach (var (source, count) in sourceCounts)
        {
            Line($"- {source}: {count}");
        }

        Line($"- 統合後(重複をまとめた後): {mergedCount}(同じ綴りの重複 {sourceCounts.Values.Sum() - mergedCount - r.Rejected.Count} 件をまとめた)");
        Line($"- 収録: **{r.Set.Words.Count}**(Tier 1: {CountTier(r, 1)} / Tier 2: {CountTier(r, 2)} / Tier 3: {CountTier(r, 3)})");
        if (CountTier(r, 1) is var tier1 && (tier1 < Tier1Min || tier1 > Tier1Max))
        {
            Line($"- **注意**: Tier 1 が目安({Tier1Min}〜{Tier1Max} 語。仕様 v1.1 14.3)の範囲外");
        }

        Line($"- 日本語モード可: {r.Set.Words.Count(w => w.JapaneseEligible)}");
        Line($"- 意味が重なるので訳に頭文字と文字数を添えた語: {r.HintedCount}");
        Line($"- 頭文字と文字数を添えても区別できない組: {r.AmbiguousGroups.Count}");
        Line();
        Line($"## 検証エラー: {r.Errors.Count}");
        foreach (var e in r.Errors.Take(50))
        {
            Line($"- {e}");
        }

        Line();
        Line("## 穴あきの作り方の内訳");
        foreach (var (rule, count) in r.BlankRules)
        {
            Line($"- {rule}: {count}");
        }

        Line();
        Line($"## 訳がまだ無い語: {r.MissingTranslations.Count}");
        Line(r.MissingTranslations.Count == 0 ? "なし" : string.Join(", ", r.MissingTranslations.Take(200)) + (r.MissingTranslations.Count > 200 ? " …" : ""));
        Line();
        Line($"## 同じ訳の語(日本語モード不可): {r.SameTranslationGroups.Count} 組");
        foreach (var g in r.SameTranslationGroups)
        {
            var ja = r.Set.Words.First(w => w.Word == g[0]).Ja;
            Line($"- {ja}: {string.Join(", ", g)}");
        }

        Line();
        Line($"## 除外: {r.Excluded.Count}");
        foreach (var group in r.Excluded.GroupBy(e => e.Reason).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            Line($"- {group.Key}({group.Count()}): {string.Join(", ", group.Select(e => e.Word))}");
        }

        Line();
        Line($"## 統合しなかった語(英字以外を含む): {r.Rejected.Count}");
        foreach (var w in r.Rejected)
        {
            Line($"- {w.Word}({w.Source})");
        }

        Line();
        Line($"## 付加データにあるが元リストに無い語: {r.UnknownCuratedWords.Count}");
        Line(r.UnknownCuratedWords.Count == 0 ? "なし" : string.Join(", ", r.UnknownCuratedWords));
        return sb.ToString();
    }

    private static int CountTier(BuildResult r, int tier) => r.Set.Words.Count(w => w.PriorityTier == tier);
}
