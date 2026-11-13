using System.Collections.Immutable;
using System.Text.Json.Serialization;

using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

namespace EikenSpelling.Core.Application;

/// <summary>出題一覧の1件(QuestionSelector または呼び出し側が作る)。</summary>
public sealed record PlannedQuestion(string WordId, string Word, LearningMode Mode, ImmutableArray<LearningMode> EligibleModes);

/// <summary>セッションの列の1件。列の位置が通し番号(questionIndex)。</summary>
public sealed record SessionQuestion(
    string WordId, string Word, LearningMode Mode, AttemptType Attempt, ImmutableArray<LearningMode> EligibleModes);

/// <summary>回答の1件。Answers[i] は Questions[i] への回答。入力は持たない(誤答入力は学習記録に残る)。</summary>
public sealed record SessionAnswer(bool IsCorrect, LearningState? StateBefore, LearningState? StateAfter);

/// <summary>
/// 1回の練習(通常問題 → 誤答の再練習)。変更不可で、SessionManager が新しい値を返す。
/// 位置・通常問題数・再練習回数は Questions と Answers から求める(保存しない)。
/// </summary>
public sealed record PracticeSession
{
    public required Guid SessionId { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>開始時の設定(v1.2 7章)。途中で設定を変えても、このセッションはこの値で動く。</summary>
    public required LearningSettings Settings { get; init; }

    public ImmutableArray<SessionQuestion> Questions { get; init; } = [];

    public ImmutableArray<SessionAnswer> Answers { get; init; } = [];

    /// <summary>今の位置(= 回答済みの数 = 次の問題の通し番号)。</summary>
    [JsonIgnore]
    public int Position => Answers.Length;

    [JsonIgnore]
    public int NormalCount => Questions.Count(q => q.Attempt == AttemptType.Normal);

    [JsonIgnore]
    public bool IsCompleted => Answers.Length >= Questions.Length;
}
