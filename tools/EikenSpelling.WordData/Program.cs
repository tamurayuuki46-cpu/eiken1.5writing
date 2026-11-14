using System.Text;

using EikenSpelling.WordData;

// 使い方: dotnet run --project tools/EikenSpelling.WordData -- <リポジトリのフォルダ> [--allow-missing] [--report-only]
//   --allow-missing: 訳のない語を除いて組み立てる(訳を作っている途中用)。words.json は書かない
//   --report-only:   words.json を書かず data/report.md だけを書く
const string Usage = "使い方: EikenSpelling.WordData <リポジトリのフォルダ> [--allow-missing] [--report-only]";
if (args.Length < 1)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

var root = Path.GetFullPath(args[0]);
var options = new RunOptions(
    AllowMissingTranslations: args.Contains("--allow-missing", StringComparer.Ordinal),
    ReportOnly: args.Contains("--report-only", StringComparer.Ordinal));

var (result, counts, mergedCount) = WordDataPipeline.Build(root, options.AllowMissingTranslations);
var utf8 = new UTF8Encoding(false);
File.WriteAllText(WordDataPipeline.ReportPath(root), Output.Report(result, counts, mergedCount), utf8);

if (result.Errors.Count > 0)
{
    Console.Error.WriteLine($"検証エラーが {result.Errors.Count} 件あるため words.json は書きません(data/report.md を見てください)。");
    return 1;
}

if (options.ShouldWriteWordsJson(result))
{
    File.WriteAllText(WordDataPipeline.WordsJsonPath(root), Output.WordsJson(result.Set), utf8);
}
else
{
    Console.WriteLine("words.json は書いていません(--allow-missing または --report-only)。");
}

Console.WriteLine($"収録 {result.Set.Words.Count} 語 / 訳なし {result.MissingTranslations.Count} / 同じ訳 {result.SameTranslationGroups.Count} 組 / 除外 {result.Excluded.Count}");
return 0;
