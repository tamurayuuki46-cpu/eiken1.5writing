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
        // 明示的な null は読込エラーにする(null のまま WordSetValidator へ流さない)
        RespectNullableAnnotations = true,
    };

    /// <summary>読み込めない単語データ(壊れている・必須項目の欠け・null を含む・新しすぎる)なら InvalidDataException だけを投げる。</summary>
    public static WordSet Parse(string json)
    {
        try
        {
            return EnsureSupported(JsonSerializer.Deserialize<WordSet>(json, JsonOptions));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("単語データを読み込めません。", ex);
        }
    }

    public static async Task<WordSet> LoadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        try
        {
            return EnsureSupported(await JsonSerializer.DeserializeAsync<WordSet>(stream, JsonOptions, cancellationToken));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("単語データを読み込めません。", ex);
        }
    }

    private static WordSet EnsureSupported(WordSet? set)
    {
        if (set is null)
        {
            throw new InvalidDataException("単語データが空です。");
        }

        // リストの要素の null は RespectNullableAnnotations では検出されないため個別に確認する
        if (set.Words.Any(w => w is null || w.Sources.Any(s => s is null)))
        {
            throw new InvalidDataException("単語データに空(null)の要素があります。");
        }

        if (set.SchemaVersion > WordSet.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"単語データのschemaVersion {set.SchemaVersion} はこのアプリ(対応: {WordSet.CurrentSchemaVersion}まで)では読めません。");
        }

        return set;
    }
}
