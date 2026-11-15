using System.Collections.Immutable;

using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

namespace EikenSpelling.Core.Application;

/// <summary>
/// Answer の結果。保存には Progress と SessionToSave(完了なら null)を使う。Session は結果画面用(完了でもそのまま)。
/// 学習記録は Progress の1つだけを持つ(取り違えて整理前の記録を保存しないため)。
/// </summary>
public sealed record SessionAnswerResult(
    PracticeSession Session,
    PracticeSession? SessionToSave,
    LearningProgress Progress,
    bool IsCorrect,
    string CorrectWord,
    AnswerChange Change,
    bool Completed)
{
    /// <summary>後回しの「答えを見る」による不正解(入力は無い)。</summary>
    public bool Revealed { get; init; }
}

/// <summary>1回の回答で単語の状態がどう変わったか(画面の表示用)。</summary>
public sealed record AnswerChange(
    LearningState? StateBefore, LearningState? StateAfter, bool BecameMastered, bool ReturnedToWeak, bool ReviewAdvanced);

/// <summary>
/// 練習の進行(design.md 4章)。呼び出し側は Answer が返した学習記録とセッションを1回で保存し、
/// 失敗したら同じ値を保存し直す(Answer を同じ問題でもう一度呼ばない)。
/// 未完了のセッションがあるときは、先に Abandon してから Start する。
/// </summary>
public static class SessionManager
{
    public static PracticeSession Start(
        Guid sessionId, DateTimeOffset startedAt, IReadOnlyList<PlannedQuestion> plan, LearningSettings settings, VocabularySetScope? scope = null)
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

        if (scope?.Problem() is { } scopeProblem)
        {
            throw new ArgumentException(scopeProblem, nameof(scope));
        }

        var allowed = scope?.ToSet();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var q in plan)
        {
            var problem = CheckQuestion(q?.WordId, q?.Word, q?.Mode, q?.EligibleModes)
                ?? (seen.Add(q!.WordId) ? null : "同じ単語が出題一覧に2回あります。")
                ?? (!settings.Practice.AutoMix && q.Mode != settings.Practice.ManualMode ? "手動モードの出題一覧のモードが設定と違います。" : null)
                ?? (allowed is not null && !allowed.Contains(q.WordId) ? "出題一覧に語彙セットの範囲の外の単語があります。" : null);
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
            Scope = scope,
        };
    }

    public static SessionQuestion? Current(PracticeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.CurrentIndex is { } index ? session.Questions[index] : null;
    }

    /// <summary>今の問題の段階。完了していれば null。</summary>
    public static QuestionPhase? Phase(PracticeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.CurrentIndex is not { } index)
        {
            return null;
        }

        if (session.Questions[index].Attempt == AttemptType.Retry)
        {
            return QuestionPhase.Retry;
        }

        return session.Skips.Any(s => s.QuestionIndex == index) ? QuestionPhase.Deferred : QuestionPhase.Normal;
    }

    /// <summary>
    /// 今の問題をスキップできるか: 通常の段階の問題だけ(後回し・再練習は不可)。
    /// 通常問題の単語は重複しないので、単語ごとに1回までの上限もこれで守られる。
    /// </summary>
    public static bool CanSkip(PracticeSession session) => Phase(session) == QuestionPhase.Normal;

    /// <summary>
    /// 今の通常問題を後回しにする(採点しない。学習記録は変えない)。呼び出し側は返したセッションを保存し、
    /// 失敗したら同じ値を保存し直す(Skip をもう一度呼ばない。呼ぶと次の問題をスキップしてしまう)。
    /// </summary>
    public static PracticeSession Skip(PracticeSession session, DateTimeOffset skippedAt)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!CanSkip(session))
        {
            throw new InvalidOperationException("今の問題はスキップできません(通常問題の一巡目だけスキップできます)。");
        }

        return session with { Skips = session.Skips.Add(new SessionSkip(session.CurrentIndex!.Value, skippedAt)) };
    }

    /// <summary>今の問題がフラッシュで、この提示で表示を始めた(印が今の処理した数と同じ)か。</summary>
    public static bool IsFlashShown(PracticeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Current(session)?.Mode == LearningMode.Flash && session.FlashShownStep == session.ProcessedSteps;
    }

    /// <summary>
    /// 今のフラッシュの問題に表示開始の印を付ける(採点しない)。呼び出し側は返したセッションを保存してから単語を見せる。
    /// </summary>
    public static PracticeSession MarkFlashShown(PracticeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Current(session)?.Mode != LearningMode.Flash)
        {
            throw new InvalidOperationException("今の問題はフラッシュではありません。");
        }

        return session with { FlashShownStep = session.ProcessedSteps };
    }

    /// <summary>今の問題で「答えを見る」を選べるか: 後回しの段階だけ(通常は後回しか回答、再練習は回答だけ)。</summary>
    public static bool CanReveal(PracticeSession session) => Phase(session) == QuestionPhase.Deferred;

    public static SessionAnswerResult Answer(PracticeSession session, LearningProgress progress, string input, DateTimeOffset answeredAt)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Score(session, progress, input, answeredAt, revealed: false);
    }

    /// <summary>
    /// 後回しの問題で「答えを見る」: 元の通常枠の不正解を1回だけ確定する(入力は作らない。以前の実入力は学習記録に残る)。
    /// 保存の約束は Answer と同じ(返した学習記録とセッションを保存し、失敗したら同じ値を保存し直す。Reveal をもう一度呼ばない)。
    /// 後回しの段階以外では InvalidOperationException。
    /// </summary>
    public static SessionAnswerResult Reveal(PracticeSession session, LearningProgress progress, DateTimeOffset answeredAt)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!CanReveal(session))
        {
            throw new InvalidOperationException("答えを見られるのは後回しの問題だけです。");
        }

        return Score(session, progress, "", answeredAt, revealed: true);
    }

    private static SessionAnswerResult Score(
        PracticeSession session, LearningProgress progress, string input, DateTimeOffset answeredAt, bool revealed)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(progress);
        if (session.CurrentIndex is not { } index)
        {
            throw new InvalidOperationException("このセッションは終わっています。");
        }

        var q = session.Questions[index];
        var correct = !revealed && SpellingJudge.IsCorrect(q.Word, input);

        // answerId は元の問題の番号(後回しで答えても通常枠の ID のまま)
        var recorded = AnswerRecorder.Apply(
            progress,
            new AnswerEvent(
                new AnswerId(session.SessionId, index, q.Attempt), q.WordId, q.Mode, correct, revealed ? null : input, answeredAt, revealed),
            session.Settings);
        if (recorded.IsDuplicate)
        {
            throw new InvalidOperationException("この回答は学習記録に反映済みです(セッションと学習記録が食い違っています)。");
        }

        var answers = session.Answers.Add(new SessionAnswer(correct, recorded.StateBefore, recorded.StateAfter) { Revealed = revealed });
        var updated = session with
        {
            Answers = answers,
            Questions = AppendRetries(session.Questions, session.Skips, answers, session.Settings),
        };
        var completed = updated.IsCompleted;
        var newProgress = completed ? RemoveSession(recorded.Progress, session.SessionId) : recorded.Progress;
        var change = new AnswerChange(
            recorded.StateBefore, recorded.StateAfter, recorded.BecameMastered, recorded.ReturnedToWeak, recorded.ReviewAdvanced);
        return new SessionAnswerResult(updated, completed ? null : updated, newProgress, correct, q.Word, change, completed) { Revealed = revealed };
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

    /// <summary>
    /// 通常問題・スキップ・採点から、再練習の足し方を再生して列を作り直す(検査用)。採点が列より多ければそこで止める。
    /// </summary>
    public static IReadOnlyList<SessionQuestion> Replay(
        IReadOnlyList<SessionQuestion> normalQuestions,
        IReadOnlyList<SessionSkip> skips,
        IReadOnlyList<SessionAnswer> answers,
        LearningSettings settings)
    {
        var questions = normalQuestions.ToImmutableArray();
        var skipArray = skips.ToImmutableArray();
        var soFar = ImmutableArray<SessionAnswer>.Empty;
        for (var i = 0; i < answers.Count && i < questions.Length; i++)
        {
            soFar = soFar.Add(answers[i]);
            questions = AppendRetries(questions, skipArray, soFar, settings);
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
    /// 再練習の足し方(design.md 4章): すべての通常問題(後回しを含む)を採点し終えたら誤答した単語を通常問題の順に、
    /// 再練習で誤答したら回数が RetryCount 未満のとき同じ単語・同じモードを最後に足す。answers の最後が今回の採点。
    /// </summary>
    private static ImmutableArray<SessionQuestion> AppendRetries(
        ImmutableArray<SessionQuestion> questions, ImmutableArray<SessionSkip> skips, ImmutableArray<SessionAnswer> answers, LearningSettings settings)
    {
        var retryCount = settings.Practice.RetryCount;
        if (retryCount <= 0 || answers.IsEmpty)
        {
            return questions;
        }

        var order = SessionOrder.Of(questions, skips);
        var index = order[answers.Length - 1];
        var last = answers[^1];
        var q = questions[index];
        var normalCount = questions.Count(x => x.Attempt == AttemptType.Normal);

        if (q.Attempt == AttemptType.Normal && answers.Length == normalCount)
        {
            var wrong = new HashSet<int>(Enumerable.Range(0, normalCount).Where(k => !answers[k].IsCorrect).Select(k => order[k]));
            var retries = Enumerable.Range(0, normalCount)
                .Where(wrong.Contains)
                .Select(i => questions[i] with
                {
                    Attempt = AttemptType.Retry,
                    Mode = RetryModeRule.Choose(questions[i].Mode, questions[i].EligibleModes, settings.Practice.AutoMix),
                });
            return questions.AddRange(retries);
        }

        if (q.Attempt == AttemptType.Retry && !last.IsCorrect
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
