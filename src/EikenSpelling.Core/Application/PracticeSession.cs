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

/// <summary>
/// 採点の1件。Answers[k] は採点の順(<see cref="PracticeSession.AnswerOrder"/>)の k 番目の問題への採点。
/// 入力は持たない(誤答入力は学習記録に残る)。
/// </summary>
public sealed record SessionAnswer(bool IsCorrect, LearningState? StateBefore, LearningState? StateAfter);

/// <summary>通常問題のスキップ(後回し)の1件。採点ではないので学習記録には何も残さない。QuestionIndex は通常問題の番号。</summary>
public sealed record SessionSkip(int QuestionIndex, DateTimeOffset SkippedAt);

/// <summary>出題の段階(UI v0.3 の段階別操作)。</summary>
public enum QuestionPhase
{
    /// <summary>通常問題の一巡目。スキップできる。</summary>
    Normal,

    /// <summary>スキップした通常問題(後回し)。スキップできない。</summary>
    Deferred,

    /// <summary>誤答の追加再練習。スキップできない。</summary>
    Retry,
}

/// <summary>
/// 1回の練習(通常問題 → 後回し → 誤答の再練習)。変更不可で、SessionManager が新しい値を返す。
/// 進行は最小の台帳 Skips(スキップした順)と採点 Answers(採点した順)から求める(位置・段階は保存しない)。
/// 採点の順は スキップしなかった通常問題(番号順)→ スキップした通常問題(スキップ順)→ 再練習(番号順)。
/// Skips が空なら従来(schema v1)どおり Answers[i] が Questions[i] への採点になる。
/// </summary>
public sealed record PracticeSession
{
    public required Guid SessionId { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>開始時の設定(v1.2 7章)。途中で設定を変えても、このセッションはこの値で動く。</summary>
    public required LearningSettings Settings { get; init; }

    public ImmutableArray<SessionQuestion> Questions { get; init; } = [];

    public ImmutableArray<SessionAnswer> Answers { get; init; } = [];

    /// <summary>スキップした通常問題(スキップした順)。v1 の文書には無い(= 空)。</summary>
    public ImmutableArray<SessionSkip> Skips { get; init; } = [];

    /// <summary>
    /// フラッシュの表示を始めた提示(そのときの処理した数 = Answers.Length + Skips.Length)。
    /// 単語を見せる前に保存する(再起動後に単語を再表示しないため。UI v0.3 フラッシュ途中再開 FA)。無ければ null(書かない)。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? FlashShownStep { get; init; }

    /// <summary>処理した数(採点 + スキップ)。提示ごとに1つずつ増える。</summary>
    [JsonIgnore]
    public int ProcessedSteps => Answers.Length + Skips.Length;

    [JsonIgnore]
    public int NormalCount => Questions.Count(q => q.Attempt == AttemptType.Normal);

    [JsonIgnore]
    public bool IsCompleted => Answers.Length >= Questions.Length;

    /// <summary>採点の順に並べた問題の番号(Questions の位置)。Answers[k] は AnswerOrder[k] への採点。</summary>
    [JsonIgnore]
    public ImmutableArray<int> AnswerOrder => SessionOrder.Of(Questions, Skips);

    /// <summary>今の問題の番号(Questions の位置)。完了していれば null。</summary>
    [JsonIgnore]
    public int? CurrentIndex => IsCompleted ? null : AnswerOrder[Answers.Length];
}

/// <summary>採点の順(PracticeSession の説明を参照)。</summary>
internal static class SessionOrder
{
    public static ImmutableArray<int> Of(ImmutableArray<SessionQuestion> questions, ImmutableArray<SessionSkip> skips)
    {
        var normalCount = questions.Count(q => q.Attempt == AttemptType.Normal);
        var skipped = skips.Select(s => s.QuestionIndex).ToList();
        var skippedSet = skipped.ToHashSet();
        return
        [
            .. Enumerable.Range(0, normalCount).Where(i => !skippedSet.Contains(i)),
            .. skipped,
            .. Enumerable.Range(normalCount, questions.Length - normalCount),
        ];
    }
}
