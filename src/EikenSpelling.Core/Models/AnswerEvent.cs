using System.Text.Json.Serialization;

namespace EikenSpelling.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<AttemptType>))]
public enum AttemptType
{
    /// <summary>通常問題。</summary>
    Normal,

    /// <summary>誤答後の追加再練習(v1.0 6.2)。記録だけ残し、状態・克服・再確認には影響しない。</summary>
    Retry,
}

/// <summary>
/// 回答ごとの固定ID(v1.1 11.1)。QuestionIndex は再練習を含むセッション内の通し番号で、
/// 同じ単語の2回目の再練習にも別の番号を振る。
/// </summary>
public readonly record struct AnswerId(Guid SessionId, int QuestionIndex, AttemptType AttemptType);

/// <summary>1回の回答。回答日時は呼び出し側が TimeProvider から作る。</summary>
public sealed record AnswerEvent(
    AnswerId Id,
    string WordId,
    LearningMode Mode,
    bool IsCorrect,
    string? Input,
    DateTimeOffset AnsweredAt);
