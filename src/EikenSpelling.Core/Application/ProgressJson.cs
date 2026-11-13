using System.Text.Json;

using EikenSpelling.Core.Models;

namespace EikenSpelling.Core.Application;

/// <summary>progress.json の文字列との変換。ファイルの読み書きは EikenSpelling.Persistence が行う。</summary>
public static class ProgressJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(ProgressDocument document) => JsonSerializer.Serialize(document, Options);

    /// <summary>
    /// 全体を変換する前に schemaVersion だけを読む(新しい版は形が変わっている可能性があるため)。
    /// JSON として読めない・版が無い・数でないなら InvalidDataException。
    /// </summary>
    public static int ReadSchemaVersion(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    if (string.Equals(property.Name, "schemaVersion", StringComparison.OrdinalIgnoreCase)
                        && property.Value.ValueKind == JsonValueKind.Number
                        && property.Value.TryGetInt32(out var version))
                    {
                        return version;
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("学習記録を読み込めません。", ex);
        }

        throw new InvalidDataException("学習記録に schemaVersion がありません。");
    }

    /// <summary>
    /// 壊れている・型違い・明示的な null(項目・値・要素)を含む・新しすぎる版なら InvalidDataException だけを投げる。
    /// 知らない項目は無視し、無い項目は初期値で補う。
    /// </summary>
    public static ProgressDocument Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("学習記録が空です。");
        }

        var version = ReadSchemaVersion(json);
        if (version > ProgressDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"学習記録の schemaVersion {version} はこのアプリ(対応: {ProgressDocument.CurrentSchemaVersion}まで)では読めません。");
        }

        ProgressDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ProgressDocument>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("学習記録を読み込めません。", ex);
        }

        if (document is null)
        {
            throw new InvalidDataException("学習記録が空です。");
        }

        // 辞書の値の null は RespectNullableAnnotations では検出されないため個別に確認する
        foreach (var word in document.Progress.Words.Values)
        {
            if (word is null || word.Modes.Values.Any(m => m is null))
            {
                throw new InvalidDataException("学習記録に空(null)の値があります。");
            }
        }

        return document with
        {
            Progress = document.Progress with { Words = document.Progress.Words.WithComparers(StringComparer.Ordinal) },
        };
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
            RespectNullableAnnotations = true,
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
