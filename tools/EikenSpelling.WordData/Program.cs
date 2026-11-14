using System.Text;

using EikenSpelling.WordData;

// 使い方: dotnet run --project tools/EikenSpelling.WordData -- <リポジトリのフォルダ> [--allow-missing] [--report-only]
//   --allow-missing: 訳のない語を除いて生成する(訳を作っている途中用)。無ければ訳のない語は誤り
//   --report-only: words.json を書かず data/report.md だけを書く(途中で同梱のデータを置き換えないため)
if (args.Length < 1)
{
    Console.Error.WriteLine("使い方: EikenSpelling.WordData <リポジトリのフォルダ> [--allow-missing]");
    return 2;
}

var root = Path.GetFullPath(args[0]);
var allowMissing = args.Contains("--allow-missing", StringComparer.Ordinal);
var reportOnly = args.Contains("--report-only", StringComparer.Ordinal); // 作業の途中: words.json を書かずにレポートだけ
var sources = Path.Combine(root, "data", "sources");

var ngslStats = SourceLists.ReadNgslStats(SourceLists.ReadUtf8(Path.Combine(sources, "NGSL_12_stats.csv")));
var ngslSup = SourceLists.ReadLemmatized(SourceLists.ReadUtf8(Path.Combine(sources, "SUP_lemmatized.csv")));
var nawl = SourceLists.ReadLemmatized(SourceLists.ReadLatin1(Path.Combine(sources, "NAWL_12_lemmatized_for_research.csv")));
var bsl = SourceLists.ReadBslStats(SourceLists.ReadUtf8(Path.Combine(sources, "BSL_120_stats.csv")));

var merged = WordMerger.Merge([.. ngslStats, .. ngslSup], nawl, bsl);
var ngslMax = ngslStats.Max(w => w.Rank ?? 0);
var curation = Curation.Load(Path.Combine(root, "data", "curation"));
var result = WordSetBuilder.Build(merged, ngslMax, curation, new BuildOptions("2026.11-5k-1", "NGSL-1.2_NAWL-1.2_BSL-1.2", allowMissing));

var counts = new Dictionary<string, int>
{
    ["NGSL 1.2"] = ngslStats.Count,
    ["NGSL 1.2 補足"] = ngslSup.Count,
    ["NAWL 1.2"] = nawl.Count,
    ["BSL 1.2"] = bsl.Count,
};
var utf8 = new UTF8Encoding(false);
File.WriteAllText(Path.Combine(root, "data", "report.md"), Output.Report(result, counts, merged.Words.Count), utf8);

if (result.Errors.Count > 0)
{
    Console.Error.WriteLine($"検証エラーが {result.Errors.Count} 件あるため words.json は書きません(data/report.md を見てください)。");
    return 1;
}

if (!reportOnly)
{
    File.WriteAllText(Path.Combine(root, "src", "EikenSpelling.Wpf", "Data", "words.json"), Output.WordsJson(result.Set), utf8);
}

Console.WriteLine($"収録 {result.Set.Words.Count} 語 / 訳なし {result.MissingTranslations.Count} / 同じ訳 {result.SameTranslationGroups.Count} 組 / 除外 {result.Excluded.Count}");
return 0;
