using System.Text.Json;
using System.Text.Json.Nodes;

using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

namespace EikenSpelling.Core.Application;

/// <summary>progress.json の文字列との変換。ファイルの読み書きは EikenSpelling.Persistence が行う。</summary>
public static class ProgressJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static readonly JsonDocumentOptions NodeOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// 学習記録とセッションを別々に変換する。学習記録が読めなければ InvalidDataException。
    /// セッションだけが読めない(必須項目の欠け・null・型違い)ときは Session = null・SessionUnreadable = true。
    /// </summary>
    public static ParsedProgress ParseDetailed(string json)
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
        JsonNode? sessionNode = null;
        try
        {
            if (JsonNode.Parse(json, documentOptions: NodeOptions) is not JsonObject root)
            {
                throw new InvalidDataException("学習記録の形が正しくありません。");
            }

            // セッションは学習記録とは別に変換する(セッションの不備で学習記録まで読めなくしない)
            foreach (var key in root.Select(p => p.Key).Where(k => string.Equals(k, "session", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                sessionNode = root[key];
                root.Remove(key);
            }

            document = root.Deserialize<ProgressDocument>(Options);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException("学習記録を読み込めません。", ex);
        }

        if (document is null)
        {
            throw new InvalidDataException("学習記録が空です。");
        }

        if (document.SchemaVersion != version)
        {
            throw new InvalidDataException("学習記録の schemaVersion が食い違っています。");
        }

        // 辞書の値の null は RespectNullableAnnotations では検出されないため個別に確認する
        foreach (var word in document.Progress.Words.Values)
        {
            if (word is null || word.Modes.Values.Any(m => m is null))
            {
                throw new InvalidDataException("学習記録に空(null)の値があります。");
            }
        }

        var (session, unreadable) = ReadSession(sessionNode);
        return new ParsedProgress(
            document with
            {
                Progress = document.Progress with { Words = document.Progress.Words.WithComparers(StringComparer.Ordinal) },
                Session = session,
            },
            unreadable);
    }

    private static (PracticeSession? Session, bool Unreadable) ReadSession(JsonNode? node)
    {
        if (node is null)
        {
            return (null, false);
        }

        try
        {
            var session = node.Deserialize<PracticeSession>(Options);
            if (session is null || session.Settings is null || LearningSettingsJson.HasNullElements(session.Settings)
                || session.Questions.IsDefault || session.Answers.IsDefault
                || session.Questions.Any(q => q is null) || session.Answers.Any(a => a is null))
            {
                return (null, true);
            }

            return (session, false);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return (null, true);
        }
    }

    public static string Serialize(ProgressDocument document) => JsonSerializer.Serialize(document, Options);

    /// <summary>
    /// 全体を変換する前に schemaVersion だけを読む(新しい版は形が変わっている可能性があるため)。
    /// JSON として読めない・版が無い・数でない・食い違って複数あるなら InvalidDataException。
    /// </summary>
    public static int ReadSchemaVersion(string json) =>
        JsonVersionReader.Read(json, "schemaVersion") ?? throw new InvalidDataException("学習記録に schemaVersion がありません。");

    /// <summary>
    /// 壊れている・型違い・明示的な null(項目・値・要素)を含む・新しすぎる版なら InvalidDataException だけを投げる。
    /// 知らない項目は無視し、無い項目は初期値で補う。
    /// </summary>
    public static ProgressDocument Parse(string json) => ParseDetailed(json).Document;

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

public sealed record ParsedProgress(ProgressDocument Document, bool SessionUnreadable);
