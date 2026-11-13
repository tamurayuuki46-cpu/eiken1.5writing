using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Core.Tests;

/// <summary>⑥ 実装の3体レビュー(2026-11-13)の P1・P2 対応。</summary>
public class AnswerRecorderReviewFixTests
{
    private static readonly DateOnly D = Jst.Day0;

    [Fact]
    public void Weak_word_stays_weak_on_mistake_after_threshold_raised()
    {
        var l = new Learner();
        l.MakeWeak("tax", D);
        l.Settings = l.Settings with { WeakWordScore = l.Settings.WeakWordScore with { MarkWeakAfterMistakes = 3 } };

        var r = l.Answer("tax", Japanese, false, Jst.At(D.AddDays(1)));

        Assert.Equal(LearningState.Weak, r.StateAfter);
    }

    [Theory]
    [InlineData(false)] // 期限前
    [InlineData(true)]  // 期限後
    public void Mastered_word_returns_to_weak_on_mistake_even_with_high_threshold(bool afterDue)
    {
        var l = new Learner();
        var masteredOn = l.MakeMastered("tax", D);
        var masteredAt = l.Word("tax").MasteredAt;
        var due = l.Word("tax").NextReviewDate!.Value;
        l.Settings = l.Settings with { WeakWordScore = l.Settings.WeakWordScore with { MarkWeakAfterMistakes = 5 } };

        var r = l.Answer("tax", Blank, false, Jst.At(afterDue ? due.AddDays(1) : masteredOn.AddDays(1)));

        Assert.Equal(LearningState.Weak, r.StateAfter);
        Assert.True(r.ReturnedToWeak);
        var w = l.Word("tax");
        Assert.Equal(0, w.ReviewStage);
        Assert.Null(w.NextReviewDate);
        Assert.Equal(masteredAt, w.MasteredAt);
    }

    [Fact]
    public void Streak_equals_required_days_when_mastered_after_lowering()
    {
        var l = new Learner();
        l.Settings = l.Settings with { Mastery = l.Settings.Mastery with { RequiredCorrectDays = 3 } };
        l.MakeWeak("tax", D);
        l.Answer("tax", Japanese, true, Jst.At(D.AddDays(1)));
        l.Answer("tax", Japanese, true, Jst.At(D.AddDays(2)));
        l.Settings = l.Settings with { Mastery = l.Settings.Mastery with { RequiredCorrectDays = 1 } };

        l.Answer("tax", Japanese, true, Jst.At(D.AddDays(3)));

        Assert.Equal(1, l.Word("tax").MasteryStreak);
    }

    [Fact]
    public void Retry_correct_on_due_date_does_not_advance_review()
    {
        var l = new Learner();
        l.MakeMastered("tax", D);
        var before = l.Word("tax");

        var r = l.Answer("tax", Japanese, true, Jst.At(before.NextReviewDate!.Value), AttemptType.Retry);

        Assert.False(r.ReviewAdvanced);
        var w = l.Word("tax");
        Assert.Equal(before.ReviewStage, w.ReviewStage);
        Assert.Equal(before.NextReviewDate, w.NextReviewDate);
        Assert.Equal(before.MasteredAt, w.MasteredAt);
    }

    [Fact]
    public void Empty_session_or_negative_index_is_rejected()
    {
        var l = new Learner();
        var empty = new AnswerEvent(new AnswerId(Guid.Empty, 0, AttemptType.Normal), "tax", Japanese, true, "tax", Jst.At(D));
        var negative = new AnswerEvent(new AnswerId(Guid.NewGuid(), -1, AttemptType.Normal), "tax", Japanese, true, "tax", Jst.At(D));

        Assert.Throws<ArgumentException>(() => l.Submit(empty));
        Assert.Throws<ArgumentException>(() => l.Submit(negative));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Empty_or_null_word_id_is_rejected(string? wordId)
    {
        var l = new Learner();

        Assert.Throws<ArgumentException>(() => l.Answer(wordId!, Japanese, true, Jst.At(D)));
    }

    [Fact]
    public void Last_wrong_input_stays_paired_with_newest_time()
    {
        var l = new Learner();
        l.Answer("tax", Japanese, false, Jst.At(D.AddDays(2)), input: "new");

        l.Answer("tax", Japanese, false, Jst.At(D), input: "old");

        Assert.Equal("new", l.Word("tax").Mode(Japanese).LastWrongInput);
    }

    [Fact]
    public void Ending_schedule_is_reported_as_advanced()
    {
        var l = new Learner();
        l.Settings = l.Settings with { Mastery = l.Settings.Mastery with { ReviewIntervalsDays = [7], RepeatLastInterval = false } };
        l.MakeMastered("tax", D);

        var r = l.Answer("tax", Japanese, true, Jst.At(l.Word("tax").NextReviewDate!.Value));

        Assert.True(r.ReviewAdvanced);
        Assert.Null(l.Word("tax").NextReviewDate);
    }

    [Fact]
    public void Due_list_uses_ordinal_order_for_same_date()
    {
        var l = new Learner();
        l.MakeMastered("a", D);
        l.MakeMastered("B", D);
        var due = l.Word("a").NextReviewDate!.Value;

        Assert.Equal(["B", "a"], ReviewScheduler.DueWordIds(l.Progress, due));
    }

    [Theory]
    [InlineData(-1)]  // 上限の1日前に届く
    [InlineData(0)]   // ちょうど上限
    [InlineData(1)]   // 上限を1日超える
    public void Next_date_around_date_only_max(int overshoot)
    {
        var start = DateOnly.MaxValue.AddDays(-10);
        var days = 10 + overshoot;
        var settings = new EikenSpelling.Core.Settings.MasterySettings { ReviewIntervalsDays = [days] };

        var (_, first) = ReviewScheduler.FirstReview(start, settings);
        var (_, advanced) = ReviewScheduler.Advance(1, start, settings);

        var expected = overshoot < 0 ? DateOnly.MaxValue.AddDays(-1) : DateOnly.MaxValue;
        Assert.Equal(expected, first);
        Assert.Equal(expected, advanced);
    }
}
