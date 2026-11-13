using System.Collections.Immutable;

using EikenSpelling.Core.Models;

namespace EikenSpelling.Core.Application;

/// <summary>結果画面に出す単語。</summary>
public sealed record SummaryWord(string WordId, string Word);

/// <summary>単語ごとの再練習の結果。Attempts は回答済みの再練習の回数。</summary>
public sealed record RetryResult(string WordId, string Word, int Attempts, bool FinallyCorrect);

/// <summary>結果画面に出す「苦手状態の変化」(v1.0 16.1)。この3種類だけを出す。</summary>
public enum StateChangeKind
{
    /// <summary>記録なし・通常 → 苦手。</summary>
    BecameWeak,

    /// <summary>→ 克服済み。</summary>
    Mastered,

    /// <summary>克服済み → 苦手。</summary>
    ReturnedToWeak,
}

public sealed record StateChange(string WordId, string Word, StateChangeKind Kind);

/// <summary>
/// 結果画面の集計(app-flow design 4.7)。セッションだけから求める。回答ごとの状態の変化は保存されているので、
/// 再開をはさんでも同じ結果になる。未完了のセッションは回答済みの分だけで数える。
/// </summary>
public sealed record SessionSummary(
    int NormalCount,
    int NormalAnswered,
    int NormalCorrect,
    ImmutableArray<SummaryWord> MistakenWords,
    ImmutableArray<RetryResult> Retries,
    ImmutableArray<StateChange> StateChanges)
{
    public static SessionSummary From(PracticeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var answered = session.Questions.Take(session.Answers.Length)
            .Zip(session.Answers, (q, a) => (Question: q, Answer: a))
            .ToList();
        var normal = answered.Where(x => x.Question.Attempt == AttemptType.Normal).ToList();

        var mistaken = normal.Where(x => !x.Answer.IsCorrect)
            .Select(x => new SummaryWord(x.Question.WordId, x.Question.Word))
            .ToImmutableArray();

        var retries = answered.Where(x => x.Question.Attempt == AttemptType.Retry)
            .GroupBy(x => x.Question.WordId, StringComparer.Ordinal)
            .Select(g => new RetryResult(g.Key, g.First().Question.Word, g.Count(), g.Last().Answer.IsCorrect))
            .ToImmutableArray();

        var changes = answered
            .GroupBy(x => x.Question.WordId, StringComparer.Ordinal)
            .Select(g => ToChange(g.Key, g.First().Question.Word, g.First().Answer.StateBefore, g.Last().Answer.StateAfter))
            .OfType<StateChange>()
            .ToImmutableArray();

        return new SessionSummary(
            session.NormalCount, normal.Count, normal.Count(x => x.Answer.IsCorrect), mistaken, retries, changes);
    }

    private static StateChange? ToChange(string wordId, string word, LearningState? before, LearningState? after)
    {
        StateChangeKind? kind = (before, after) switch
        {
            (LearningState.Mastered, LearningState.Weak) => StateChangeKind.ReturnedToWeak,
            (not LearningState.Weak, LearningState.Weak) => StateChangeKind.BecameWeak,
            (not LearningState.Mastered, LearningState.Mastered) => StateChangeKind.Mastered,
            _ => null,
        };
        return kind is null ? null : new StateChange(wordId, word, kind.Value);
    }
}
