using System.Collections.Immutable;
using System.Diagnostics;

using EikenSpelling.Core.Application;
using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

using Xunit.Abstractions;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Core.Tests;

/// <summary>克服済み単語の定期再確認(v1.0 8章、design.md 4章 7)。</summary>
public class ReviewTests
{
    private static readonly DateOnly D = Jst.Day0;

    private static int Interval(Learner l, int index) => l.Settings.Mastery.ReviewIntervalsDays[index];

    [Fact]
    public void Correct_before_due_date_only_records()
    {
        var l = new Learner();
        var m = l.MakeMastered("tax", D);
        var due = l.Word("tax").NextReviewDate!.Value;

        var r = l.Answer("tax", Japanese, true, Jst.At(due.AddDays(-1)));

        Assert.False(r.ReviewAdvanced);
        Assert.Equal(1, l.Word("tax").ReviewStage);
        Assert.Equal(due, l.Word("tax").NextReviewDate);
        Assert.Equal(m.AddDays(Interval(l, 0)), due);
    }

    [Fact]
    public void Correct_on_due_date_advances_stage()
    {
        var l = new Learner();
        l.MakeMastered("tax", D);
        var due = l.Word("tax").NextReviewDate!.Value;

        var r = l.Answer("tax", Flash, true, Jst.At(due)); // どのモードでも進める

        Assert.True(r.ReviewAdvanced);
        Assert.Equal(2, l.Word("tax").ReviewStage);
        Assert.Equal(due.AddDays(Interval(l, 1)), l.Word("tax").NextReviewDate);
    }

    [Fact]
    public void Overdue_review_counts_next_date_from_answered_date()
    {
        var l = new Learner();
        l.MakeMastered("tax", D);
        var answered = l.Word("tax").NextReviewDate!.Value.AddDays(3);

        l.Answer("tax", Blank, true, Jst.At(answered));

        Assert.Equal(answered.AddDays(Interval(l, 1)), l.Word("tax").NextReviewDate);
    }

    [Fact]
    public void Default_schedule_is_7_14_30_then_every_30()
    {
        var l = new Learner();
        var day = l.MakeMastered("tax", D);
        var expected = new List<int>();
        var actual = new List<int>();

        for (var i = 0; i < 4; i++)
        {
            var next = l.Word("tax").NextReviewDate!.Value;
            actual.Add(next.DayNumber - day.DayNumber);
            day = next;
            l.Answer("tax", Japanese, true, Jst.At(day));
        }

        expected.AddRange([7, 14, 30, 30]);
        Assert.Equal(expected, actual);
        Assert.Equal(3, l.Word("tax").ReviewStage); // 段階の上限は間隔の数
    }

    [Fact]
    public void Jst_midnight_is_due_date()
    {
        var l = new Learner();
        l.MakeMastered("tax", D);
        var due = l.Word("tax").NextReviewDate!.Value;
        var dayBefore = due.AddDays(-1);

        var r1 = l.Answer("tax", Japanese, true, new DateTimeOffset(dayBefore.Year, dayBefore.Month, dayBefore.Day, 14, 59, 59, TimeSpan.Zero));
        var r2 = l.Answer("tax", Japanese, true, new DateTimeOffset(dayBefore.Year, dayBefore.Month, dayBefore.Day, 15, 0, 0, TimeSpan.Zero));

        Assert.False(r1.ReviewAdvanced);
        Assert.True(r2.ReviewAdvanced);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Mistake_before_or_after_due_returns_to_weak(bool afterDue)
    {
        var l = new Learner();
        var m = l.MakeMastered("tax", D);
        var due = l.Word("tax").NextReviewDate!.Value;

        var r = l.Answer("tax", Blank, false, Jst.At(afterDue ? due.AddDays(1) : m.AddDays(1)));

        Assert.True(r.ReturnedToWeak);
        var w = l.Word("tax");
        Assert.Equal(LearningState.Weak, w.State);
        Assert.Equal(0, w.ReviewStage);
        Assert.Null(w.NextReviewDate);
        Assert.Equal(0, w.MasteryStreak);
    }

    [Theory]
    [InlineData(true, new[] { 7, 7, 7 })]
    [InlineData(false, new[] { 7 })]
    public void Single_interval_repeats_or_ends(bool repeat, int[] expectedGaps)
    {
        var l = new Learner();
        l.Settings = l.Settings with { Mastery = l.Settings.Mastery with { ReviewIntervalsDays = [7], RepeatLastInterval = repeat } };
        var day = l.MakeMastered("tax", D);
        var gaps = new List<int>();

        while (gaps.Count < 3 && l.Word("tax").NextReviewDate is { } next)
        {
            gaps.Add(next.DayNumber - day.DayNumber);
            day = next;
            l.Answer("tax", Japanese, true, Jst.At(day));
        }

        Assert.Equal(expectedGaps, gaps);
        Assert.Equal(1, l.Word("tax").ReviewStage);
    }

    [Fact]
    public void Without_repeat_schedule_ends_after_last_stage_and_later_correct_only_records()
    {
        var l = new Learner();
        l.Settings = l.Settings with { Mastery = l.Settings.Mastery with { RepeatLastInterval = false } };
        l.MakeMastered("tax", D);
        for (var i = 0; i < 3; i++)
        {
            l.Answer("tax", Japanese, true, Jst.At(l.Word("tax").NextReviewDate!.Value));
        }

        Assert.Null(l.Word("tax").NextReviewDate);
        Assert.Equal(LearningState.Mastered, l.Word("tax").State);

        var r = l.Answer("tax", Japanese, true, Jst.At(D.AddDays(400)));

        Assert.False(r.ReviewAdvanced);
        Assert.Null(l.Word("tax").NextReviewDate);
        Assert.Equal(3, l.Word("tax").ReviewStage);
    }

    [Fact]
    public void Changed_intervals_keep_existing_date_and_apply_from_next_step()
    {
        var l = new Learner();
        l.MakeMastered("tax", D);
        var due = l.Word("tax").NextReviewDate!.Value;

        l.Settings = l.Settings with { Mastery = l.Settings.Mastery with { ReviewIntervalsDays = [3, 5, 9] } };

        Assert.Equal(due, l.Word("tax").NextReviewDate);
        l.Answer("tax", Japanese, true, Jst.At(due));
        Assert.Equal(due.AddDays(5), l.Word("tax").NextReviewDate);
    }

    [Theory]
    [InlineData(5)]   // 間隔の数(3)より大きい: 設定で間隔を減らした場合
    [InlineData(-1)]  // 手で書き換えた不正な値
    public void Out_of_range_stage_does_not_throw(int stage)
    {
        var l = new Learner();
        l.MakeMastered("tax", D);
        var w = l.Word("tax");
        l.Progress = l.Progress with { Words = l.Progress.Words.SetItem("tax", w with { ReviewStage = stage }) };

        var r = l.Answer("tax", Japanese, true, Jst.At(w.NextReviewDate!.Value));

        Assert.True(r.ReviewAdvanced);
        Assert.InRange(l.Word("tax").ReviewStage, 1, 3);
    }

    [Fact]
    public void Next_date_is_capped_at_date_only_max()
    {
        var nearMax = DateOnly.MaxValue.AddDays(-3);

        var (_, next) = ReviewScheduler.FirstReview(nearMax, new MasterySettings());

        Assert.Equal(DateOnly.MaxValue, next);
    }
}

/// <summary>期限が来た再確認対象の一覧(R8)。</summary>
public class DueReviewListTests
{
    private static readonly DateOnly Today = new(2026, 12, 1);

    private static WordProgress Mastered(string id, DateOnly? next) =>
        new() { WordId = id, State = LearningState.Mastered, MasteryStreak = 2, ReviewStage = 1, NextReviewDate = next };

    private static LearningProgress Of(params WordProgress[] words) =>
        LearningProgress.Empty with { Words = words.ToImmutableDictionary(w => w.WordId, StringComparer.Ordinal) };

    [Fact]
    public void Includes_up_to_today_oldest_first_then_word_id()
    {
        var p = Of(
            Mastered("c", Today),
            Mastered("b", Today.AddDays(-1)),
            Mastered("a", Today),
            Mastered("tomorrow", Today.AddDays(1)));

        Assert.Equal(["b", "a", "c"], ReviewScheduler.DueWordIds(p, Today));
    }

    [Fact]
    public void Excludes_non_mastered_and_ended_schedules()
    {
        var p = Of(
            Mastered("ended", null),
            new WordProgress { WordId = "weak", State = LearningState.Weak },
            new WordProgress { WordId = "normal", State = LearningState.Normal });

        Assert.Empty(ReviewScheduler.DueWordIds(p, Today));
    }

    [Fact]
    public void Empty_progress_has_no_due_words()
    {
        Assert.Empty(ReviewScheduler.DueWordIds(LearningProgress.Empty, Today));
    }

    [Fact]
    public void Is_due_matches_list()
    {
        Assert.True(ReviewScheduler.IsDue(Mastered("a", Today), Today));
        Assert.False(ReviewScheduler.IsDue(Mastered("a", Today.AddDays(1)), Today));
        Assert.False(ReviewScheduler.IsDue(Mastered("a", null), Today));
    }
}

/// <summary>約5,000語の記録でも1回の反映が遅くならないことの目安(閾値は置かず時間を出力するだけ)。</summary>
public class AnswerRecorderPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public void Apply_on_5000_words_is_fast_enough_to_observe()
    {
        var builder = ImmutableDictionary.CreateBuilder<string, WordProgress>(StringComparer.Ordinal);
        for (var i = 0; i < 5000; i++)
        {
            builder[$"w{i}"] = new WordProgress { WordId = $"w{i}", State = LearningState.Weak };
        }

        var progress = LearningProgress.Empty with { Words = builder.ToImmutable() };
        var answer = new AnswerEvent(new AnswerId(Guid.NewGuid(), 0, AttemptType.Normal), "w2500", Japanese, true, "w", Jst.At(Jst.Day0));

        var sw = Stopwatch.StartNew();
        var r = AnswerRecorder.Apply(progress, answer, DefaultSettings.Create());
        sw.Stop();

        output.WriteLine($"5,000語の記録への1回の反映: {sw.Elapsed.TotalMilliseconds:F2} ms");
        Assert.Equal(1, r.Progress.Word("w2500")!.MasteryStreak);
    }
}
