using System.Collections.Immutable;

using EikenSpelling.Core.Application;
using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Core.Tests;

/// <summary>セッションのテスト用の道具。</summary>
internal static class S
{
    public static readonly Guid Id = new("22222222-2222-2222-2222-222222222222");
    public static readonly ImmutableArray<LearningMode> All = [Japanese, Audio, Blank, Flash];

    public static PlannedQuestion Q(string word, LearningMode mode = Japanese, params LearningMode[] eligible) =>
        new(word, word, mode, eligible.Length == 0 ? All : [.. eligible]);

    public static LearningSettings Settings(int retry = 1, bool autoMix = true, LearningMode manual = Japanese) =>
        DefaultSettings.Create() with
        {
            Practice = DefaultSettings.Create().Practice with { RetryCount = retry, AutoMix = autoMix, ManualMode = manual },
        };

    public static PracticeSession Start(LearningSettings? settings = null, params PlannedQuestion[] plan) =>
        SessionManager.Start(Id, Jst.At(Jst.Day0, 9), plan, settings ?? Settings());

    public static PlannedQuestion[] Words(int n) => Enumerable.Range(0, n).Select(i => Q($"w{i}")).ToArray();
}

/// <summary>セッションを回答で進める。</summary>
internal sealed class Runner(PracticeSession session, LearningProgress? progress = null)
{
    public PracticeSession Session { get; private set; } = session;

    public LearningProgress Progress { get; private set; } = progress ?? LearningProgress.Empty;

    public SessionAnswerResult? Last { get; private set; }

    public SessionAnswerResult Answer(bool correct, DateTimeOffset? at = null)
    {
        var q = SessionManager.Current(Session)!;
        Last = SessionManager.Answer(Session, Progress, correct ? q.Word : q.Word + "x", at ?? Jst.At(Jst.Day0, 10));
        Session = Last.Session;
        Progress = Last.Progress;
        return Last;
    }

    public void AnswerAll(bool correct)
    {
        while (!Session.IsCompleted)
        {
            Answer(correct);
        }
    }

    public IReadOnlyList<(string Word, LearningMode Mode, AttemptType Attempt)> Sequence() =>
        Session.Questions.Select(q => (q.WordId, q.Mode, q.Attempt)).ToList();
}

public class SessionStartTests
{
    [Fact]
    public void Start_keeps_plan_order_and_settings_snapshot()
    {
        var settings = S.Settings(retry: 2);

        var s = S.Start(settings, S.Q("a"), S.Q("b", Audio));

        Assert.Equal(S.Id, s.SessionId);
        Assert.Same(settings, s.Settings);
        Assert.Equal(["a", "b"], s.Questions.Select(q => q.WordId));
        Assert.All(s.Questions, q => Assert.Equal(AttemptType.Normal, q.Attempt));
        Assert.Equal(0, s.Position);
        Assert.Equal("a", SessionManager.Current(s)!.WordId);
    }

    public static TheoryData<string> InvalidPlans => ["empty", "101", "emptyId", "blankWord", "blankId", "duplicate", "undefinedMode", "emptyEligible", "undefinedEligible", "ineligibleMode"];

    [Theory]
    [MemberData(nameof(InvalidPlans))]
    public void Invalid_plan_is_rejected(string kind)
    {
        PlannedQuestion[] plan = kind switch
        {
            "empty" => [],
            "101" => S.Words(101),
            "blankWord" => [new("a", " ", Japanese, S.All)],
            "blankId" => [new("", "a", Japanese, S.All)],
            "duplicate" => [S.Q("a"), S.Q("a", Audio)],
            "undefinedMode" => [new("a", "a", (LearningMode)9, S.All)],
            "emptyEligible" => [new("a", "a", Japanese, [])],
            "undefinedEligible" => [new("a", "a", Japanese, [Japanese, (LearningMode)9])],
            "ineligibleMode" => [S.Q("a", Audio, Japanese, Blank)],
            _ => [S.Q("a")],
        };
        var id = kind == "emptyId" ? Guid.Empty : S.Id;

        Assert.Throws<ArgumentException>(() => SessionManager.Start(id, Jst.At(Jst.Day0), plan, S.Settings()));
    }

    [Fact]
    public void Hundred_questions_is_allowed()
    {
        Assert.Equal(100, S.Start(null, S.Words(100)).Questions.Length);
    }

    [Fact]
    public void Invalid_settings_snapshot_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => S.Start(S.Settings(retry: 5), S.Q("a")));
    }

    [Fact]
    public void Manual_mode_must_match_plan()
    {
        Assert.Throws<ArgumentException>(() => S.Start(S.Settings(autoMix: false, manual: Japanese), S.Q("a", Blank)));
        Assert.NotNull(S.Start(S.Settings(autoMix: false, manual: Blank), S.Q("a", Blank)));
    }
}

public class SessionFlowTests
{
    [Fact]
    public void Single_question_correct_completes_immediately()
    {
        var r = new Runner(S.Start(null, S.Q("a")));

        var result = r.Answer(true);

        Assert.True(result.Completed);
        Assert.Null(result.SessionToSave);
        Assert.Null(SessionManager.Current(r.Session));
    }

    [Fact]
    public void Single_question_wrong_adds_one_retry()
    {
        var r = new Runner(S.Start(null, S.Q("a")));

        var result = r.Answer(false);

        Assert.False(result.Completed);
        Assert.NotNull(result.SessionToSave);
        var next = SessionManager.Current(r.Session)!;
        Assert.Equal(("a", AttemptType.Retry), (next.WordId, next.Attempt));
    }

    [Fact]
    public void Ten_questions_with_three_wrong_end_after_thirteen()
    {
        var r = new Runner(S.Start(null, S.Words(10)));
        var wrong = new HashSet<int> { 2, 5, 7 };

        for (var i = 0; i < 10; i++)
        {
            r.Answer(!wrong.Contains(i));
        }

        Assert.Equal(["w2", "w5", "w7"], r.Session.Questions.Skip(10).Select(q => q.WordId)); // 通常問題の順
        r.AnswerAll(true);
        Assert.Equal(13, r.Session.Questions.Length);
        Assert.True(r.Last!.Completed);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 20)]
    [InlineData(2, 30)]
    [InlineData(3, 40)]
    public void All_wrong_repeats_retries_up_to_retry_count(int retry, int total)
    {
        var r = new Runner(S.Start(S.Settings(retry), S.Words(10)));

        r.AnswerAll(false);

        Assert.Equal(total, r.Session.Questions.Length);
        Assert.All(S.Words(10), w => Assert.Equal(retry, r.Session.Questions.Count(q => q.WordId == w.WordId && q.Attempt == AttemptType.Retry)));
    }

    [Fact]
    public void Second_round_retries_come_after_first_round()
    {
        var r = new Runner(S.Start(S.Settings(2), S.Q("a"), S.Q("b")));

        r.AnswerAll(false);

        Assert.Equal(["a", "b", "a", "b", "a", "b"], r.Session.Questions.Select(q => q.WordId));
    }

    [Fact]
    public void Correct_retry_ends_that_word()
    {
        var r = new Runner(S.Start(S.Settings(3), S.Q("a"), S.Q("b")));
        r.Answer(false);
        r.Answer(false);

        r.Answer(true);  // a の1回目の再練習で正解
        r.AnswerAll(false);

        Assert.Equal(1, r.Session.Questions.Count(q => q.WordId == "a" && q.Attempt == AttemptType.Retry));
        Assert.Equal(3, r.Session.Questions.Count(q => q.WordId == "b" && q.Attempt == AttemptType.Retry));
    }

    [Fact]
    public void Retry_count_zero_never_adds_retries()
    {
        var r = new Runner(S.Start(S.Settings(0), S.Q("a")));

        Assert.True(r.Answer(false).Completed);
    }

    [Fact]
    public void Answer_ids_follow_list_positions_and_are_pruned_on_completion()
    {
        var r = new Runner(S.Start(null, S.Q("a"), S.Q("b")));
        r.Answer(false);
        r.Answer(true);

        Assert.Equal(
            [new AnswerId(S.Id, 0, AttemptType.Normal), new AnswerId(S.Id, 1, AttemptType.Normal)],
            r.Progress.AppliedAnswerIds.OrderBy(i => i.QuestionIndex));

        r.Answer(true); // 再練習(通し番号 2)で完了
        Assert.Equal(1, r.Progress.Word("a")!.Mode(r.Session.Questions[2].Mode).RetryCorrect);
        Assert.Empty(r.Progress.AppliedAnswerIds);
    }

    [Fact]
    public void Answers_record_state_changes()
    {
        var r = new Runner(S.Start(null, S.Q("a")));

        r.Answer(false);

        Assert.Equal(new SessionAnswer(false, null, LearningState.Weak), r.Session.Answers[0]);
    }

    [Fact]
    public void Answering_completed_session_or_null_input_throws()
    {
        var r = new Runner(S.Start(null, S.Q("a")));
        r.Answer(true);

        Assert.Throws<InvalidOperationException>(() => SessionManager.Answer(r.Session, r.Progress, "a", Jst.At(Jst.Day0)));
        var fresh = S.Start(null, S.Q("a"));
        Assert.Throws<ArgumentNullException>(() => SessionManager.Answer(fresh, LearningProgress.Empty, null!, Jst.At(Jst.Day0)));
    }

    [Fact]
    public void Blank_input_is_wrong()
    {
        var result = SessionManager.Answer(S.Start(null, S.Q("a")), LearningProgress.Empty, "  ", Jst.At(Jst.Day0));

        Assert.False(result.IsCorrect);
        Assert.Equal("a", result.CorrectWord);
    }

    [Fact]
    public void Replaying_same_question_on_new_progress_throws()
    {
        var start = S.Start(null, S.Q("a"), S.Q("b"));
        var first = SessionManager.Answer(start, LearningProgress.Empty, "a", Jst.At(Jst.Day0));

        Assert.Throws<InvalidOperationException>(() => SessionManager.Answer(start, first.Progress, "a", Jst.At(Jst.Day0)));
    }

    [Fact]
    public void Original_values_are_not_changed()
    {
        var start = S.Start(null, S.Q("a"));

        SessionManager.Answer(start, LearningProgress.Empty, "x", Jst.At(Jst.Day0));

        Assert.Empty(start.Answers);
        Assert.Single(start.Questions);
    }
}

public class RetryModeTests
{
    [Theory]
    [InlineData(Japanese, Audio)]
    [InlineData(Audio, Japanese)]
    [InlineData(Blank, Japanese)]
    [InlineData(Flash, Japanese)]
    public void Auto_mix_uses_first_other_eligible_mode(LearningMode original, LearningMode expected)
    {
        Assert.Equal(expected, RetryModeRule.Choose(original, S.All, autoMix: true));
    }

    [Fact]
    public void Auto_mix_skips_ineligible_and_falls_back_to_same()
    {
        Assert.Equal(Blank, RetryModeRule.Choose(Japanese, [Japanese, Blank, Flash], autoMix: true));
        Assert.Equal(Flash, RetryModeRule.Choose(Flash, [Flash], autoMix: true));
    }

    [Fact]
    public void Manual_uses_same_mode()
    {
        Assert.Equal(Blank, RetryModeRule.Choose(Blank, S.All, autoMix: false));
    }

    [Fact]
    public void Session_retries_use_rule_and_keep_same_mode_later()
    {
        var r = new Runner(S.Start(S.Settings(2), S.Q("a", Japanese, Japanese, Blank)));

        r.AnswerAll(false);

        Assert.Equal([Japanese, Blank, Blank], r.Session.Questions.Select(q => q.Mode));
    }

    [Fact]
    public void Manual_session_retries_use_same_mode()
    {
        var r = new Runner(S.Start(S.Settings(1, autoMix: false, manual: Flash), S.Q("a", Flash)));

        r.AnswerAll(false);

        Assert.Equal([Flash, Flash], r.Session.Questions.Select(q => q.Mode));
    }
}

public class SessionSnapshotAndDateTests
{
    [Fact]
    public void Snapshot_settings_are_used_even_if_current_settings_differ()
    {
        // スナップショット: 再練習1回・必要正答日数1。セッションには今の設定を渡す口が無いので、スナップショットだけが使われる
        var snapshot = S.Settings(1) with { Mastery = DefaultSettings.Create().Mastery with { RequiredCorrectDays = 1 } };
        var weak = AnswerRecorder.Apply(LearningProgress.Empty,
            new AnswerEvent(new AnswerId(Guid.NewGuid(), 0, AttemptType.Normal), "a", Japanese, false, "x", Jst.At(Jst.Day0.AddDays(-1))),
            DefaultSettings.Create()).Progress;
        var r = new Runner(S.Start(snapshot, S.Q("a")), weak);

        r.Answer(true);

        Assert.Equal(LearningState.Mastered, r.Progress.Word("a")!.State); // 既定(2日)なら克服しない
    }

    [Fact]
    public void Normal_answer_after_midnight_uses_answered_date()
    {
        var weak = AnswerRecorder.Apply(LearningProgress.Empty,
            new AnswerEvent(new AnswerId(Guid.NewGuid(), 0, AttemptType.Normal), "a", Japanese, false, "x", Jst.At(Jst.Day0.AddDays(-1))),
            DefaultSettings.Create()).Progress;
        var session = SessionManager.Start(S.Id, Jst.At(Jst.Day0, 23, 59), [S.Q("a")], S.Settings());
        var r = new Runner(session, weak);

        r.Answer(true, Jst.At(Jst.Day0.AddDays(1), 0, 1));

        Assert.Equal(Jst.Day0.AddDays(1), r.Progress.Word("a")!.LastEligibleCorrectDate);
    }

    [Fact]
    public void Retry_after_midnight_does_not_count_for_mastery()
    {
        var session = SessionManager.Start(S.Id, Jst.At(Jst.Day0, 23, 50), [S.Q("a")], S.Settings());
        var r = new Runner(session);
        r.Answer(false, Jst.At(Jst.Day0, 23, 59));

        r.Answer(true, Jst.At(Jst.Day0.AddDays(1), 0, 1)); // 再練習で正解

        var w = r.Progress.Word("a")!;
        Assert.Equal(LearningState.Weak, w.State);
        Assert.Equal(0, w.MasteryStreak);
    }
}

public class SessionAbandonAndPruneTests
{
    [Fact]
    public void Abandon_mid_retry_keeps_records_and_prunes_ids()
    {
        var r = new Runner(S.Start(S.Settings(2), S.Q("a"), S.Q("b")));
        r.Answer(false);
        r.Answer(true);
        r.Answer(false); // a の再練習で誤答

        var progress = SessionManager.Abandon(r.Session, r.Progress);

        Assert.Empty(progress.AppliedAnswerIds);
        Assert.Equal(1, progress.Word("a")!.NormalMistakes);
        Assert.Equal(1, progress.Word("a")!.Modes.Values.Sum(m => m.RetryWrong));
        Assert.NotNull(progress.Word("b"));
    }

    [Fact]
    public void Prune_keeps_only_given_session()
    {
        var other = Guid.NewGuid();
        var p = LearningProgress.Empty with
        {
            AppliedAnswerIds = [new AnswerId(S.Id, 0, AttemptType.Normal), new AnswerId(other, 0, AttemptType.Normal)],
        };

        Assert.Equal([new AnswerId(S.Id, 0, AttemptType.Normal)], SessionManager.PruneAnswerIds(p, S.Id).AppliedAnswerIds);
        Assert.Empty(SessionManager.PruneAnswerIds(p, null).AppliedAnswerIds);
    }
}
