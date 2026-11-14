namespace EikenSpelling.WordData;

/// <summary>実行のしかた(Program の引数)。</summary>
internal sealed record RunOptions(bool AllowMissingTranslations, bool ReportOnly)
{
    /// <summary>
    /// words.json を書いてよいか。訳がそろっていない(--allow-missing)・レポートだけ(--report-only)・検証エラーありのときは書かない。
    /// 訳のない語を抜いたまま同梱のデータを置き換えると、収録していた単語 ID が消えて学習記録が結び付かなくなるため。
    /// </summary>
    public bool ShouldWriteWordsJson(BuildResult result) => !AllowMissingTranslations && !ReportOnly && result.Errors.Count == 0;
}

/// <summary>Program と同じ手順で、リポジトリの元データから単語データを組み立てる(テストから作り直しを確かめるため)。</summary>
internal static class WordDataPipeline
{
    public const string WordSetVersion = "2026.11-5k-1";
    public const string SourceVersion = "NGSL-1.2_NAWL-1.2_BSL-1.2";

    public static string WordsJsonPath(string root) => Path.Combine(root, "src", "EikenSpelling.Wpf", "Data", "words.json");

    public static string ReportPath(string root) => Path.Combine(root, "data", "report.md");

    public static (BuildResult Result, IReadOnlyDictionary<string, int> Counts, int MergedCount) Build(string root, bool allowMissingTranslations)
    {
        var sources = Path.Combine(root, "data", "sources");
        var ngslStats = SourceLists.ReadNgslStats(SourceLists.ReadUtf8(Path.Combine(sources, "NGSL_12_stats.csv")));
        var ngslSup = SourceLists.ReadLemmatized(SourceLists.ReadUtf8(Path.Combine(sources, "SUP_lemmatized.csv")));
        var nawl = SourceLists.ReadLemmatized(SourceLists.ReadLatin1(Path.Combine(sources, "NAWL_12_lemmatized_for_research.csv")));
        var bsl = SourceLists.ReadBslStats(SourceLists.ReadUtf8(Path.Combine(sources, "BSL_120_stats.csv")));
        if (ngslStats.Count == 0 || nawl.Count == 0 || bsl.Count == 0)
        {
            throw new InvalidDataException("元リストのどれかに語がありません(data/sources を確かめてください)。");
        }

        var merged = WordMerger.Merge([.. ngslStats, .. ngslSup], nawl, bsl);
        var ngslMax = ngslStats.Max(w => w.Rank ?? 0);
        var curation = Curation.Load(Path.Combine(root, "data", "curation"));
        var result = WordSetBuilder.Build(merged, ngslMax, curation, new BuildOptions(WordSetVersion, SourceVersion, allowMissingTranslations));
        var counts = new Dictionary<string, int>
        {
            ["NGSL 1.2"] = ngslStats.Count,
            ["NGSL 1.2 補足"] = ngslSup.Count,
            ["NAWL 1.2"] = nawl.Count,
            ["BSL 1.2"] = bsl.Count,
        };
        return (result, counts, merged.Words.Count);
    }
}
