using System.Collections.Immutable;

using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

namespace EikenSpelling.Core.Application;

/// <summary>Answer の結果。SessionToSave は保存用(完了なら null)、Session は結果画面用(完了でもそのまま)。</summary>
public sealed record SessionAnswerResult(
    PracticeSession Session,
    PracticeSession? SessionToSave,
    LearningProgress Progress,
    bool IsCorrect,
    string CorrectWord,
    AnswerResult Recorded,
    bool Completed);

/// <summary>
/// 練習の進行(design.md 4章)。呼び出し側は Answer が返した学習記録とセッションを1回で保存し、
/// 失敗したら同じ値を保存し直す(Answer を同じ問題でもう一度呼ばない)。
/// 未完了のセッションがあるときは、先に Abandon してから Start する。
/// </summary>
public static class SessionManager
{
    public static PracticeSession Start(Guid sessionId, DateTimeOffset startedAt, IReadOnlyList<PlannedQuestion> plan, LearningSettings settings)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(settings);
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("セッションIDが空です。", nameof(sessionId));
        }

        if (plan.Count == 0 || plan.Count > SettingsLimits.MaxQuestionCount)
        {
            throw new ArgumentException($"出題一覧は1〜{SettingsLimits.MaxQuestionCount}件にしてください。", nameof(plan));
        }

        if (SettingsValidator.Validate(settings).Count > 0)
        {
            throw new ArgumentException("設定に不正な値があるため、練習を始められません。", nameof(settings));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var q in plan)
        {
            var problem = CheckQuestion(q?.WordId, q?.Word, q?.Mode, q?.EligibleModes)
                ?? (seen.Add(q!.WordId) ? null : "同じ単語が出題一覧に2回あります。")
                ?? (!settings.Practice.AutoMix && q.Mode != settings.Practice.ManualMode ? "手動モードの出題一覧のモードが設定と違います。" : null);
            if (problem is not null)
            {
                throw new ArgumentException(problem, nameof(plan));
            }
        }

        return new PracticeSession
        {
            SessionId = sessionId,
            StartedAt = startedAt,
            Settings = settings,
            Questions = [.. plan.Select(q => new SessionQuestion(q.WordId, q.Word, q.Mode, AttemptType.Normal, q.EligibleModes))],
        };
    }

    public static SessionQuestion? Current(PracticeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.IsCompleted ? null : session.Questions[session.Position];
    }

    public static SessionAnswerResult Answer(PracticeSession session, LearningProgress progress, string input, DateTimeOffset answeredAt)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(input);
        if (session.IsCompleted)
        {
            throw new InvalidOperationException("このセッションは終わっています。");
        }

        var index = session.Position;
        var q = session.Questions[index];
        var correct = SpellingJudge.IsCorrect(q.Word, input);
        var recorded = AnswerRecorder.Apply(
            progress,
            new AnswerEvent(new AnswerId(session.SessionId, index, q.Attempt), q.WordId, q.Mode, correct, input, answeredAt),
            session.Settings);
        if (recorded.IsDuplicate)
        {
            throw new InvalidOperationException("この回答は学習記録に反映済みです(セッションと学習記録が食い違っています)。");
        }

        var answers = session.Answers.Add(new SessionAnswer(correct, recorded.StateBefore, recorded.StateAfter));
        var updated = session with
        {
            Answers = answers,
            Questions = AppendRetries(session.Questions, answers, index, session.Settings),
        };
        var completed = updated.IsCompleted;
        var newProgress = completed ? RemoveSession(recorded.Progress, session.SessionId) : recorded.Progress;
        return new SessionAnswerResult(updated, completed ? null : updated, newProgress, correct, q.Word, recorded, completed);
    }

    /// <summary>途中でやめる。回答済みの記録は残し、そのセッションの answerId を消した学習記録を返す。</summary>
    public static LearningProgress Abandon(PracticeSession session, LearningProgress progress)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(progress);
        return RemoveSession(progress, session.SessionId);
    }

    /// <summary>keepSessionId 以外の answerId を消す(null なら全部)。</summary>
    public static LearningProgress PruneAnswerIds(LearningProgress progress, Guid? keepSessionId)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var kept = progress.AppliedAnswerIds.Where(id => id.SessionId == keepSessionId).ToImmutableHashSet();
        return kept.Count == progress.AppliedAnswerIds.Count ? progress : progress with { AppliedAnswerIds = kept };
    }

    /// <summary>通常問題と回答から、再練習の足し方を再生して列を作り直す(検査用)。回答が列より多ければそこで止める。</summary>
    public static IReadOnlyList<SessionQuestion> Replay(
        IReadOnlyList<SessionQuestion> normalQuestions, IReadOnlyList<SessionAnswer> answers, LearningSettings settings)
    {
        var questions = normalQuestions.ToImmutableArray();
        var soFar = ImmutableArray<SessionAnswer>.Empty;
        for (var i = 0; i < answers.Count && i < questions.Length; i++)
        {
            soFar = soFar.Add(answers[i]);
            questions = AppendRetries(questions, soFar, i, settings);
        }

        return questions;
    }

    /// <summary>1問の検査(Start と SessionValidator で共通)。問題が無ければ null。</summary>
    internal static string? CheckQuestion(string? wordId, string? word, LearningMode? mode, ImmutableArray<LearningMode>? eligible)
    {
        if (string.IsNullOrWhiteSpace(wordId) || string.IsNullOrWhiteSpace(word))
        {
            return "単語IDまたは綴りが空です。";
        }

        if (mode is not { } m || !Enum.IsDefined(m))
        {
            return "出題モードが定義外です。";
        }

        if (eligible is not { IsDefaultOrEmpty: false } modes || modes.Any(x => !Enum.IsDefined(x)))
        {
            return "使えるモードが空か、定義外の値を含んでいます。";
        }

        return modes.Contains(m) ? null : "出題モードがその単語で使えるモードにありません。";
    }

    /// <summary>
    /// 再練習の足し方(design.md 4章): 最後の通常問題に答えたら誤答した単語を通常問題の順に、
    /// 再練習で誤答したら回数が RetryCount 未満のとき同じ単語・同じモードを最後に足す。
    /// </summary>
    private static ImmutableArray<SessionQuestion> AppendRetries(
        ImmutableArray<SessionQuestion> questions, ImmutableArray<SessionAnswer> answers, int index, LearningSettings settings)
    {
        var retryCount = settings.Practice.RetryCount;
        var q = questions[index];
        var normalCount = questions.Count(x => x.Attempt == AttemptType.Normal);
        if (retryCount <= 0)
        {
            return questions;
        }

        if (q.Attempt == AttemptType.Normal && index == normalCount - 1)
        {
            var retries = Enumerable.Range(0, normalCount)
                .Where(i => !answers[i].IsCorrect)
                .Select(i => questions[i] with
                {
                    Attempt = AttemptType.Retry,
                    Mode = RetryModeRule.Choose(questions[i].Mode, questions[i].EligibleModes, settings.Practice.AutoMix),
                });
            return questions.AddRange(retries);
        }

        if (q.Attempt == AttemptType.Retry && !answers[index].IsCorrect
            && questions.Count(x => x.Attempt == AttemptType.Retry && x.WordId == q.WordId) < retryCount)
        {
            return questions.Add(q);
        }

        return questions;
    }

    private static LearningProgress RemoveSession(LearningProgress progress, Guid sessionId)
    {
        var kept = progress.AppliedAnswerIds.Where(id => id.SessionId != sessionId).ToImmutableHashSet();
        return progress with { AppliedAnswerIds = kept };
    }
}
