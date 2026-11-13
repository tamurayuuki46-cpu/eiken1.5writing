using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;

using static EikenSpelling.Core.Models.LearningMode;
using static EikenSpelling.Core.Models.LearningState;

namespace EikenSpelling.Core.Tests;

public class SessionSummaryTests
{
    private static SessionQuestion N(string w) => new(w, w + "-spell", Japanese, AttemptType.Normal, S.All);

    private static SessionQuestion R(string w) => new(w, w + "-spell", Audio, AttemptType.Retry, S.All);

    private static SessionAnswer A(bool ok, LearningState? before = Normal, LearningState? after = Normal) => new(ok, before, after);

    private static PracticeSession Session(SessionQuestion[] questions, params SessionAnswer[] answers) => new()
    {
        SessionId = S.Id,
        StartedAt = Jst.At(Jst.Day0, 9),
        Settings = S.Settings(),
        Questions = [.. questions],
        Answers = [.. answers],
    };

    [Fact]
    public void No_answers_gives_counts_of_zero_and_empty_lists()
    {
        var summary = SessionSummary.From(Session([N("a"), N("b")]));

        Assert.Equal(2, summary.NormalCount);
        Assert.Equal(0, summary.NormalAnswered);
        Assert.Equal(0, summary.NormalCorrect);
        Assert.Empty(summary.MistakenWords);
        Assert.Empty(summary.Retries);
        Assert.Empty(summary.StateChanges);
    }

    [Fact]
    public void All_correct_has_no_mistakes_or_retries()
    {
        var summary = SessionSummary.From(Session([N("a"), N("b")], A(true), A(true)));

        Assert.Equal(2, summary.NormalAnswered);
        Assert.Equal(2, summary.NormalCorrect);
        Assert.Empty(summary.MistakenWords);
        Assert.Empty(summary.Retries);
    }

    [Fact]
    public void Mistake_then_retry_correct_lists_word_and_one_successful_retry()
    {
        var summary = SessionSummary.From(Session([N("a"), N("b"), R("b")], A(true), A(false), A(true)));

        Assert.Equal(2, summary.NormalCount);
        Assert.Equal(1, summary.NormalCorrect);
        Assert.Equal([new SummaryWord("b", "b-spell")], summary.MistakenWords.ToArray());
        Assert.Equal([new RetryResult("b", "b-spell", 1, true)], summary.Retries.ToArray());
    }

    [Fact]
    public void Retries_all_wrong_counts_every_attempt_and_reports_not_correct()
    {
        var summary = SessionSummary.From(Session(
            [N("a"), N("b"), R("a"), R("b"), R("a"), R("a")],
            A(false), A(false), A(false), A(true), A(false), A(false)));

        Assert.Equal(["a", "b"], summary.MistakenWords.Select(w => w.WordId));
        Assert.Equal(
            [new RetryResult("a", "a-spell", 3, false), new RetryResult("b", "b-spell", 1, true)],
            summary.Retries.ToArray());
    }

    [Fact]
    public void Retry_count_zero_has_mistakes_but_no_retries()
    {
        var summary = SessionSummary.From(Session([N("a")], A(false)));

        Assert.Single(summary.MistakenWords);
        Assert.Empty(summary.Retries);
    }

    [Fact]
    public void State_changes_report_only_weak_mastered_and_returned_in_first_appearance_order()
    {
        var summary = SessionSummary.From(Session(
            [N("new"), N("normal"), N("first"), N("weak"), N("mastered"), N("same"), N("stillWeak")],
            A(false, null, Weak),
            A(false, Normal, Weak),
            A(true, null, Normal),
            A(true, Weak, Mastered),
            A(false, Mastered, Weak),
            A(true, Normal, Normal),
            A(false, Weak, Weak)));

        Assert.Equal(
            [
                new StateChange("new", "new-spell", StateChangeKind.BecameWeak),
                new StateChange("normal", "normal-spell", StateChangeKind.BecameWeak),
                new StateChange("weak", "weak-spell", StateChangeKind.Mastered),
                new StateChange("mastered", "mastered-spell", StateChangeKind.ReturnedToWeak),
            ],
            summary.StateChanges.ToArray());
    }

    [Fact]
    public void State_change_compares_first_before_with_last_after_of_the_word()
    {
        // 通常問題で苦手になり、再練習(状態を変えない)の記録が後に続いても「新たに苦手」1件
        var summary = SessionSummary.From(Session(
            [N("a"), R("a"), R("a")],
            A(false, Normal, Weak), A(false, Weak, Weak), A(true, Weak, Weak)));

        Assert.Equal([new StateChange("a", "a-spell", StateChangeKind.BecameWeak)], summary.StateChanges.ToArray());
    }

    [Fact]
    public void Incomplete_session_counts_only_answered_questions()
    {
        var summary = SessionSummary.From(Session([N("a"), N("b"), N("c")], A(false, null, Weak)));

        Assert.Equal(3, summary.NormalCount);
        Assert.Equal(1, summary.NormalAnswered);
        Assert.Equal(0, summary.NormalCorrect);
        Assert.Equal(["a"], summary.MistakenWords.Select(w => w.WordId));
        Assert.Single(summary.StateChanges);
    }

    [Fact]
    public void Summary_of_a_real_session_matches_what_was_answered()
    {
        var runner = new Runner(S.Start(S.Settings(retry: 1), S.Q("a"), S.Q("b"), S.Q("c")));
        runner.Answer(true);
        runner.Answer(false);
        runner.Answer(true);
        runner.Answer(true); // 再練習 b

        var summary = SessionSummary.From(runner.Session);

        Assert.True(runner.Session.IsCompleted);
        Assert.Equal(3, summary.NormalCount);
        Assert.Equal(2, summary.NormalCorrect);
        Assert.Equal(["b"], summary.MistakenWords.Select(w => w.WordId));
        Assert.Equal([new RetryResult("b", "b", 1, true)], summary.Retries.ToArray());
    }

    [Fact]
    public void Null_session_throws()
    {
        Assert.Throws<ArgumentNullException>(() => SessionSummary.From(null!));
    }
}
