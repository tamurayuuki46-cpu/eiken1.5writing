using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

namespace EikenSpelling.Core.Tests;

/// <summary>3体レビュー(2026-11-13)の P1 対応: 出題枠の境界(reviewLimits の区切りの前後・0・1・負数)。</summary>
public class QuestionPlannerBoundaryTests
{
    private static readonly IReadOnlySet<LearningMode> AllModes = Enum.GetValues<LearningMode>().ToHashSet();

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(1, 1, 0, 0)]    // 苦手枠が0なので再確認も0
    [InlineData(6, 3, 2, 1)]    // 区切り5の直後 → 上限1
    [InlineData(11, 6, 3, 2)]   // 区切り10の直後 → 上限2
    [InlineData(21, 11, 8, 2)]  // 最後の区切り20の直後 → 最後の値2
    public void Slots_around_review_limit_thresholds(int count, int newWords, int weak, int review)
    {
        var s = DefaultSettings.Create();

        var slots = QuestionPlanner.PlanSlots(count, s.Practice, s.Mastery);

        Assert.Equal(new QuestionSlots(newWords, weak, review), slots);
        Assert.Equal(count, slots.Total);
    }

    [Fact]
    public void Negative_question_count_is_rejected()
    {
        var s = DefaultSettings.Create();

        Assert.Throws<ArgumentOutOfRangeException>(() => QuestionPlanner.PlanSlots(-1, s.Practice, s.Mastery));
        Assert.Throws<ArgumentOutOfRangeException>(() => QuestionPlanner.AllocateModes(-1, new ModeWeights(), AllModes));
    }

    [Fact]
    public void Zero_questions_allocate_nothing()
    {
        Assert.All(QuestionPlanner.AllocateModes(0, new ModeWeights(), AllModes).Values, v => Assert.Equal(0, v));
    }

    [Fact]
    public void One_question_goes_to_japanese_on_tie()
    {
        var r = QuestionPlanner.AllocateModes(1, new ModeWeights(), AllModes);

        Assert.Equal(1, r[LearningMode.Japanese]);
        Assert.Equal(1, r.Values.Sum());
    }

    [Fact]
    public void Huge_weights_do_not_overflow()
    {
        var weights = new ModeWeights { Japanese = int.MaxValue, Audio = int.MaxValue, Blank = int.MaxValue, Flash = int.MaxValue };

        var r = QuestionPlanner.AllocateModes(10, weights, AllModes);

        Assert.All(r.Values, v => Assert.InRange(v, 0, 10));
        Assert.Equal(10, r.Values.Sum());
        Assert.Equal((3, 3, 2, 2), (r[LearningMode.Japanese], r[LearningMode.Audio], r[LearningMode.Blank], r[LearningMode.Flash]));
    }
}
