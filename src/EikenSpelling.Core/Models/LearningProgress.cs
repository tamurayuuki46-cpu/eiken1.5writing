using System.Collections.Immutable;

namespace EikenSpelling.Core.Models;

/// <summary>学習記録全体。変更不可で、AnswerRecorder.Apply が新しい値を返す。版は ProgressDocument が持つ。</summary>
public sealed record LearningProgress
{
    public static readonly LearningProgress Empty = new();

    public ImmutableDictionary<string, WordProgress> Words { get; init; } =
        ImmutableDictionary<string, WordProgress>.Empty.WithComparers(StringComparer.Ordinal);

    /// <summary>反映済みの回答ID(二重加算の防止。v1.1 11.2)。</summary>
    public ImmutableHashSet<AnswerId> AppliedAnswerIds { get; init; } = ImmutableHashSet<AnswerId>.Empty;

    public WordProgress? Word(string wordId) => Words.GetValueOrDefault(wordId);
}
