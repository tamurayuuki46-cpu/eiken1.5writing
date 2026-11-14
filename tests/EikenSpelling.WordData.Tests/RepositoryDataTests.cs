namespace EikenSpelling.WordData.Tests;

/// <summary>
/// リポジトリの元データ(data/sources・data/curation)から作り直した結果が、同梱の words.json と一致する(requirements D2〜D8)。
/// words.json を手で直した・付加データだけ直して作り直していない、というずれを見つける。
/// </summary>
public class RepositoryDataTests
{
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EikenSpelling.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("リポジトリのフォルダ(EikenSpelling.sln)が見つかりません。");
    }

    /// <summary>改行コードの違い(git の自動変換)は無視して比べる。</summary>
    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    [Fact]
    public void Rebuilding_from_the_repository_data_reproduces_the_bundled_words_json_exactly()
    {
        var root = RepositoryRoot();

        var (result, _, _) = WordDataPipeline.Build(root, allowMissingTranslations: false);

        Assert.Empty(result.Errors);
        Assert.Empty(result.MissingTranslations);
        Assert.Empty(result.AmbiguousGroups);
        var bundled = File.ReadAllText(WordDataPipeline.WordsJsonPath(root));
        Assert.True(
            Normalize(Output.WordsJson(result.Set)) == Normalize(bundled),
            "同梱の words.json が元データから作り直した結果と違います。`dotnet run --project tools/EikenSpelling.WordData -- .` で作り直してください。");
    }
}
