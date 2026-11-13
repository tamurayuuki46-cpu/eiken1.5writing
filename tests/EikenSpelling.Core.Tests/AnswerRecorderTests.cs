using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Core.Tests;

/// <summary>入力の検査・状態の変化・苦手登録(design.md 4章 1〜7)。</summary>
public class AnswerRecorderStateTests
{
    private static readonly DateOnly D = Jst.Day0;

    [Fact]
    public void Empty_word_id_is_rejected()
    {
        var l = new Learner();

        Assert.Throws<ArgumentException>(() => l.Answer(" ", Japanese, true, Jst.At(D)));
    }

    [Fact]
    public void Undefined_mode_or_attempt_type_is_rejected()
    {
        var l = new Learner();

        Assert.Throws<ArgumentException>(() => l.Answer("tax", (LearningMode)99, true, Jst.At(D)));
        Assert.Throws<ArgumentException>(() => l.Answer("tax", Japanese, true, Jst.At(D), (AttemptType)2));
    }

    [Fact]
    public void Retry_for_unseen_word_is_invalid()
    {
        var l = new Learner();

        Assert.Throws<InvalidOperationException>(() => l.Answer("tax", Japanese, true, Jst.At(D), AttemptType.Retry));
    }

    [Fact]
    public void First_correct_answer_makes_word_normal()
    {
        var l = new Learner();

        var r = l.Answer("tax", Blank, true, Jst.At(D));

        Assert.Null(r.StateBefore);
        Assert.Equal(LearningState.Normal, r.StateAfter);
        Assert.Equal(1, l.Word("tax").Mode(Blank).NormalCorrect);
        Assert.Equal(Jst.At(D), l.Word("tax").LastNormalAskedAt);
    }

    [Fact]
    public void First_wrong_answer_makes_word_weak()
    {
        var l = new Learner();

        var r = l.Answer("tax", Flash, false, Jst.At(D), input: "taks");

        Assert.Equal(LearningState.Weak, r.StateAfter);
        var mode = l.Word("tax").Mode(Flash);
        Assert.Equal(1, mode.NormalWrong);
        Assert.Equal("taks", mode.LastWrongInput);
        Assert.Equal(Jst.At(D), mode.LastWrongAt);
    }

    [Fact]
    public void Normal_word_becomes_weak_after_mistake()
    {
        var l = new Learner();
        l.Answer("tax", Japanese, true, Jst.At(D));

        var r = l.Answer("tax", Audio, false, Jst.At(D.AddDays(1)));

        Assert.Equal(LearningState.Normal, r.StateBefore);
        Assert.Equal(LearningState.Weak, r.StateAfter);
    }

    [Fact]
    public void Correct_on_normal_word_does_not_count_for_mastery()
    {
        var l = new Learner();
        l.Answer("tax", Japanese, true, Jst.At(D));

        l.Answer("tax", Japanese, true, Jst.At(D.AddDays(1)));

        Assert.Equal(LearningState.Normal, l.Word("tax").State);
        Assert.Equal(0, l.Word("tax").MasteryStreak);
    }

    [Fact]
    public void Weak_threshold_counts_cumulative_normal_mistakes()
    {
        var l = new Learner();
        l.Settings = l.Settings with { WeakWordScore = l.Settings.WeakWordScore with { MarkWeakAfterMistakes = 2 } };

        Assert.Equal(LearningState.Normal, l.Answer("tax", Japanese, false, Jst.At(D)).StateAfter);
        Assert.Equal(LearningState.Normal, l.Answer("tax", Japanese, true, Jst.At(D)).StateAfter);
        Assert.Equal(LearningState.Weak, l.Answer("tax", Japanese, false, Jst.At(D.AddDays(1))).StateAfter);
    }

    [Fact]
    public void Retry_mistake_is_not_counted_for_weak_threshold()
    {
        var l = new Learner();
        l.Settings = l.Settings with { WeakWordScore = l.Settings.WeakWordScore with { MarkWeakAfterMistakes = 2 } };
        l.Answer("tax", Japanese, false, Jst.At(D));
        l.Answer("tax", Blank, false, Jst.At(D), AttemptType.Retry);

        Assert.Equal(LearningState.Normal, l.Word("tax").State);
        Assert.Equal(1, l.Word("tax").NormalMistakes);
    }

    [Fact]
    public void Original_progress_is_not_changed()
    {
        var l = new Learner();
        l.Answer("tax", Japanese, true, Jst.At(D));
        var before = l.Progress;
        var beforeWord = before.Word("tax");

        l.Answer("tax", Japanese, false, Jst.At(D.AddDays(1)));

        Assert.Same(beforeWord, before.Word("tax"));
        Assert.Equal(LearningState.Normal, before.Word("tax")!.State);
        Assert.Single(before.AppliedAnswerIds);
        Assert.Equal(LearningState.Weak, l.Word("tax").State);
    }

    [Fact]
    public void Older_timestamps_do_not_overwrite_newer_ones()
    {
        var l = new Learner();
        l.Answer("tax", Japanese, false, Jst.At(D.AddDays(2)), input: "new");

        l.Answer("tax", Japanese, false, Jst.At(D), input: "old");

        var w = l.Word("tax");
        Assert.Equal(Jst.At(D.AddDays(2)), w.LastNormalAskedAt);
        Assert.Equal(Jst.At(D.AddDays(2)), w.Mode(Japanese).LastAskedAt);
        Assert.Equal(Jst.At(D.AddDays(2)), w.Mode(Japanese).LastWrongAt);
        Assert.Equal(2, w.Mode(Japanese).NormalWrong);
    }

    [Fact]
    public void Words_are_recorded_even_if_not_in_word_data()
    {
        var l = new Learner();

        l.Answer("not-in-word-set", Japanese, true, Jst.At(D));

        Assert.NotNull(l.Progress.Word("not-in-word-set"));
    }
}

/// <summary>克服判定(v1.0 7.3、design.md 4章 7)。</summary>
public class AnswerRecorderMasteryTests
{
    private static readonly DateOnly D = Jst.Day0;

    [Theory]
    [InlineData(Japanese, Japanese)]
    [InlineData(Audio, Audio)]
    [InlineData(Japanese, Audio)]
    public void Mastered_after_correct_answers_on_different_days(LearningMode first, LearningMode second)
    {
        var l = new Learner();
        l.MakeWeak("tax", D);

        var r1 = l.Answer("tax", first, true, Jst.At(D.AddDays(1)));
        var r2 = l.Answer("tax", second, true, Jst.At(D.AddDays(2)));

        Assert.False(r1.BecameMastered);
        Assert.True(r2.BecameMastered);
        var w = l.Word("tax");
        Assert.Equal(LearningState.Mastered, w.State);
        Assert.Equal(l.Settings.Mastery.RequiredCorrectDays, w.MasteryStreak); // 克服時の値を残す(v1.1 10.3)
        Assert.Equal(1, w.ReviewStage);
        Assert.Equal(D.AddDays(2).AddDays(l.Settings.Mastery.ReviewIntervalsDays[0]), w.NextReviewDate);
        Assert.Equal(Jst.At(D.AddDays(2)), w.MasteredAt);
    }

    [Fact]
    public void Same_day_correct_counts_once()
    {
        var l = new Learner();
        l.MakeWeak("tax", D);

        l.Answer("tax", Japanese, true, Jst.At(D.AddDays(1), 9));
        l.Answer("tax", Audio, true, Jst.At(D.AddDays(1), 20));

        Assert.Equal(1, l.Word("tax").MasteryStreak);
        Assert.Equal(LearningState.Weak, l.Word("tax").State);
    }

    [Fact]
    public void Same_instant_two_answers_count_once()
    {
        var l = new Learner();
        l.MakeWeak("tax", D);

        l.Answer("tax", Japanese, true, Jst.At(D.AddDays(1)));
        l.Answer("tax", Japanese, true, Jst.At(D.AddDays(1)));

        Assert.Equal(1, l.Word("tax").MasteryStreak);
        Assert.Equal(2, l.Word("tax").Mode(Japanese).NormalCorrect);
    }

    [Theory]
    [InlineData(Blank)]
    [InlineData(Flash)]
    public void Non_mastery_modes_neither_increase_nor_reset_streak(LearningMode mode)
    {
        var l = new Learner();
        l.MakeWeak("tax", D);
        l.Answer("tax", Japanese, true, Jst.At(D.AddDays(1)));

        l.Answer("tax", mode, true, Jst.At(D.AddDays(2)));

        Assert.Equal(1, l.Word("tax").MasteryStreak);
        Assert.Equal(D.AddDays(1), l.Word("tax").LastEligibleCorrectDate);
    }

    [Fact]
    public void Mistake_resets_streak()
    {
        var l = new Learner();
        l.MakeWeak("tax", D);
        l.Answer("tax", Japanese, true, Jst.At(D.AddDays(1)));

        l.Answer("tax", Blank, false, Jst.At(D.AddDays(2)));

        Assert.Equal(0, l.Word("tax").MasteryStreak);
        Assert.Null(l.Word("tax").LastEligibleCorrectDate);
        Assert.Equal(LearningState.Weak, l.Word("tax").State);
    }

    [Fact]
    public void Correct_after_mistake_on_same_day_is_first_of_new_streak()
    {
        var l = new Learner();
        l.MakeWeak("tax", D);
        l.Answer("tax", Japanese, true, Jst.At(D.AddDays(1), 9));
        l.Answer("tax", Japanese, false, Jst.At(D.AddDays(1), 10));

        l.Answer("tax", Japanese, true, Jst.At(D.AddDays(1), 11));

        Assert.Equal(1, l.Word("tax").MasteryStreak);
    }

    [Fact]
    public void Earlier_date_than_last_eligible_does_not_increase_streak()
    {
        var l = new Learner();
        l.MakeWeak("tax", D);
        l.Answer("tax", Japanese, true, Jst.At(D.AddDays(5)));

        l.Answer("tax", Japanese, true, Jst.At(D.AddDays(3)));

        Assert.Equal(1, l.Word("tax").MasteryStreak);
        Assert.Equal(D.AddDays(5), l.Word("tax").LastEligibleCorrectDate);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Required_days_come_from_settings(int required)
    {
        var l = new Learner();
        l.Settings = l.Settings with { Mastery = l.Settings.Mastery with { RequiredCorrectDays = required } };
        l.MakeWeak("tax", D);

        for (var i = 1; i < required; i++)
        {
            l.Answer("tax", Japanese, true, Jst.At(D.AddDays(i)));
            Assert.Equal(LearningState.Weak, l.Word("tax").State); // n-1 回では克服しない
        }

        Assert.True(l.Answer("tax", Japanese, true, Jst.At(D.AddDays(required))).BecameMastered);
    }

    [Fact]
    public void Required_days_one_masters_on_correct_right_after_same_day_mistake()
    {
        var l = new Learner();
        l.Settings = l.Settings with { Mastery = l.Settings.Mastery with { RequiredCorrectDays = 1 } };
        l.MakeWeak("tax", D);

        Assert.True(l.Answer("tax", Japanese, true, Jst.At(D, 13)).BecameMastered);
    }

    [Fact]
    public void Lowering_required_days_masters_on_next_eligible_correct()
    {
        var l = new Learner();
        l.Settings = l.Settings with { Mastery = l.Settings.Mastery with { RequiredCorrectDays = 3 } };
        l.MakeWeak("tax", D);
        l.Answer("tax", Japanese, true, Jst.At(D.AddDays(1)));
        l.Answer("tax", Japanese, true, Jst.At(D.AddDays(2)));

        l.Settings = l.Settings with { Mastery = l.Settings.Mastery with { RequiredCorrectDays = 1 } };

        Assert.True(l.Answer("tax", Japanese, true, Jst.At(D.AddDays(3))).BecameMastered);
    }

    [Fact]
    public void Jst_midnight_separates_days()
    {
        var l = new Learner();
        l.MakeWeak("tax", D);
        var lastSecond = new DateTimeOffset(D.Year, D.Month, D.Day, 14, 59, 59, TimeSpan.Zero); // JST 23:59:59
        var midnight = new DateTimeOffset(D.Year, D.Month, D.Day, 15, 0, 0, TimeSpan.Zero);      // JST 翌 0:00:00

        l.Answer("tax", Japanese, true, lastSecond);
        var r = l.Answer("tax", Japanese, true, midnight);

        Assert.True(r.BecameMastered);
        Assert.Equal(D.AddDays(1), l.Word("tax").LastEligibleCorrectDate);
    }

    [Fact]
    public void Other_offsets_are_converted_to_jst_date()
    {
        var l = new Learner();
        l.MakeWeak("tax", D);
        var newYork = TimeSpan.FromHours(-5);

        // NY 11/1 10:00 = JST 11/2 0:00 → 11/2 扱い
        l.Answer("tax", Japanese, true, new DateTimeOffset(D.Year, D.Month, D.Day, 10, 0, 0, newYork));

        Assert.Equal(D.AddDays(1), l.Word("tax").LastEligibleCorrectDate);
    }

    [Fact]
    public void Mastered_then_weak_then_mastered_restarts_at_stage_1()
    {
        var l = new Learner();
        var masteredOn = l.MakeMastered("tax", D);
        var r = l.Answer("tax", Japanese, false, Jst.At(masteredOn.AddDays(1)));
        Assert.True(r.ReturnedToWeak);
        Assert.Equal(Jst.At(masteredOn), l.Word("tax").MasteredAt); // 苦手に戻っても残す

        var again = masteredOn.AddDays(1);
        l.Answer("tax", Japanese, true, Jst.At(again.AddDays(1)));
        l.Answer("tax", Japanese, true, Jst.At(again.AddDays(2)));

        var w = l.Word("tax");
        Assert.Equal(LearningState.Mastered, w.State);
        Assert.Equal(1, w.ReviewStage);
        Assert.Equal(again.AddDays(2).AddDays(l.Settings.Mastery.ReviewIntervalsDays[0]), w.NextReviewDate);
        Assert.Equal(Jst.At(again.AddDays(2)), w.MasteredAt);
    }
}

/// <summary>追加再練習と二重加算の防止(R3・R6)。</summary>
public class AnswerRecorderRetryAndDuplicateTests
{
    private static readonly DateOnly D = Jst.Day0;

    [Fact]
    public void Retry_answers_only_update_records()
    {
        var l = new Learner();
        l.MakeWeak("tax", D);
        l.Answer("tax", Japanese, true, Jst.At(D.AddDays(1)));
        var before = l.Word("tax");

        l.Answer("tax", Audio, true, Jst.At(D.AddDays(2)), AttemptType.Retry);
        l.Answer("tax", Blank, false, Jst.At(D.AddDays(2)), AttemptType.Retry, input: "tacks");

        var w = l.Word("tax");
        Assert.Equal(before.State, w.State);
        Assert.Equal(before.MasteryStreak, w.MasteryStreak);
        Assert.Equal(before.LastEligibleCorrectDate, w.LastEligibleCorrectDate);
        Assert.Equal(before.NextReviewDate, w.NextReviewDate);
        Assert.Equal(before.LastNormalAskedAt, w.LastNormalAskedAt);
        Assert.Equal(1, w.Mode(Audio).RetryCorrect);
        Assert.Equal(0, w.Mode(Audio).NormalCorrect);
        Assert.Equal(1, w.Mode(Blank).RetryWrong);
        Assert.Equal("tacks", w.Mode(Blank).LastWrongInput);
        Assert.Equal(Jst.At(D.AddDays(2)), w.Mode(Blank).LastAskedAt);
    }

    [Fact]
    public void Retry_mistake_does_not_return_mastered_word_to_weak()
    {
        var l = new Learner();
        var masteredOn = l.MakeMastered("tax", D);

        var r = l.Answer("tax", Japanese, false, Jst.At(masteredOn.AddDays(30)), AttemptType.Retry);

        Assert.False(r.ReturnedToWeak);
        Assert.Equal(LearningState.Mastered, l.Word("tax").State);
    }

    [Theory]
    [InlineData(AttemptType.Normal)]
    [InlineData(AttemptType.Retry)]
    public void Same_answer_id_is_applied_once(AttemptType type)
    {
        var l = new Learner();
        l.Answer("tax", Japanese, false, Jst.At(D));
        var answer = new AnswerEvent(new AnswerId(l.Session, 100, type), "tax", Japanese, true, "tax", Jst.At(D.AddDays(1)));

        var first = l.Submit(answer);
        var second = l.Submit(answer);

        Assert.False(first.IsDuplicate);
        Assert.True(second.IsDuplicate);
        Assert.Same(first.Progress, second.Progress);
        var mode = l.Word("tax").Mode(Japanese);
        Assert.Equal(1, type == AttemptType.Normal ? mode.NormalCorrect : mode.RetryCorrect);
    }

    [Fact]
    public void Ids_differing_in_one_part_are_different_answers()
    {
        var l = new Learner();
        l.Answer("tax", Japanese, false, Jst.At(D));
        var baseId = new AnswerId(l.Session, 100, AttemptType.Retry);
        AnswerId[] ids =
        [
            baseId,
            baseId with { SessionId = Guid.NewGuid() },
            baseId with { QuestionIndex = 101 },
            baseId with { AttemptType = AttemptType.Normal },
        ];

        foreach (var id in ids)
        {
            Assert.False(l.Submit(new AnswerEvent(id, "tax", Blank, true, "tax", Jst.At(D))).IsDuplicate);
        }
    }

    [Fact]
    public void Second_retry_of_same_word_with_new_index_is_recorded()
    {
        var l = new Learner();
        l.Answer("tax", Japanese, false, Jst.At(D));

        l.Answer("tax", Blank, false, Jst.At(D), AttemptType.Retry);
        l.Answer("tax", Flash, true, Jst.At(D), AttemptType.Retry);

        Assert.Equal(1, l.Word("tax").Mode(Blank).RetryWrong);
        Assert.Equal(1, l.Word("tax").Mode(Flash).RetryCorrect);
    }
}

/// <summary>正答率(v1.0 11.3)。</summary>
public class ModeAccuracyTests
{
    [Fact]
    public void Unseen_or_retry_only_mode_is_not_learned_yet()
    {
        var l = new Learner();
        l.Answer("tax", Japanese, false, Jst.At(Jst.Day0));
        l.Answer("tax", Blank, true, Jst.At(Jst.Day0), AttemptType.Retry);

        Assert.Null(l.Word("tax").Mode(Audio).Accuracy);
        Assert.Null(l.Word("tax").Mode(Blank).Accuracy);
    }

    [Fact]
    public void Accuracy_is_per_mode_and_normal_only()
    {
        var l = new Learner();
        l.Answer("tax", Flash, true, Jst.At(Jst.Day0));
        l.Answer("tax", Flash, false, Jst.At(Jst.Day0));
        l.Answer("tax", Flash, false, Jst.At(Jst.Day0), AttemptType.Retry);
        l.Answer("tax", Japanese, true, Jst.At(Jst.Day0));

        Assert.Equal(0.5, l.Word("tax").Mode(Flash).Accuracy);
        Assert.Equal(1.0, l.Word("tax").Mode(Japanese).Accuracy);
    }
}
