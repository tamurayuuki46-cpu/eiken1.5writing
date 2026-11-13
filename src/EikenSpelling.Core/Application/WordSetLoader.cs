using System.Text.Json;

using EikenSpelling.Core.Models;

namespace EikenSpelling.Core.Application;

/// <summary>単語データ(words.json)の読み込み。ファイル操作は呼び出し側(WPF)が行い、ここでは文字列かストリームを受け取る。</summary>
public static class WordSetLoader
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    public static WordSet Parse(string json)
    {
        var set = JsonSerializer.Deserialize<WordSet>(json, JsonOptions)
            ?? throw new InvalidDataException("単語データが空です。");
        return EnsureSupported(set);
    }

    public static async Task<WordSet> LoadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var set = await JsonSerializer.DeserializeAsync<WordSet>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("単語データが空です。");
        return EnsureSupported(set);
    }

    private static WordSet EnsureSupported(WordSet set)
    {
        if (set.SchemaVersion > WordSet.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"単語データのschemaVersion {set.SchemaVersion} はこのアプリ(対応: {WordSet.CurrentSchemaVersion}まで)では読めません。");
        }

        return set;
    }
}
