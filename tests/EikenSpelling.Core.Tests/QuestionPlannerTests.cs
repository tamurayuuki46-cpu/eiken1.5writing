using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

namespace EikenSpelling.Core.Tests;

public class QuestionPlannerTests
{
    private static readonly IReadOnlySet<LearningMode> AllModes = Enum.GetValues<LearningMode>().ToHashSet();

    private static readonly IReadOnlySet<LearningMode> NoAudio =
        new HashSet<LearningMode> { LearningMode.Japanese, LearningMode.Blank, LearningMode.Flash };

    private static (int Ja, int Audio, int Blank, int Flash) Allocate(
        int count, ModeWeights? weights = null, IReadOnlySet<LearningMode>? modes = null)
    {
        var r = QuestionPlanner.AllocateModes(count, weights ?? new ModeWeights(), modes ?? AllModes);
        return (r[LearningMode.Japanese], r[LearningMode.Audio], r[LearningMode.Blank], r[LearningMode.Flash]);
    }

    [Theory]
    [InlineData(5, 2, 1, 1, 1)]
    [InlineData(10, 3, 3, 2, 2)]
    [InlineData(20, 6, 6, 4, 4)]
    public void Default_ratio_matches_v1_1_table(int count, int ja, int audio, int blank, int flash)
    {
        Assert.Equal((ja, audio, blank, flash), Allocate(count));
    }

    [Fact]
    public void Without_audio_the_remaining_modes_are_reallocated_by_largest_remainder()
    {
        // v1.1 8.3: 日本語3:穴あき2:フラッシュ2 で10問 → 4/3/3
        Assert.Equal((4, 0, 3, 3), Allocate(10, modes: NoAudio));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(33)]
    public void Allocation_always_sums_to_question_count(int count)
    {
        var (ja, audio, blank, flash) = Allocate(count);
        Assert.Equal(count, ja + audio + blank + flash);

        var noAudio = Allocate(count, modes: NoAudio);
        Assert.Equal(count, noAudio.Ja + noAudio.Blank + noAudio.Flash);
        Assert.Equal(0, noAudio.Audio);
    }

    [Fact]
    public void Changing_weights_changes_allocation()
    {
        Assert.Equal((10, 0, 0, 0), Allocate(10, new ModeWeights { Japanese = 1, Audio = 0, Blank = 0, Flash = 0 }));
        Assert.Equal((5, 5, 0, 0), Allocate(10, new ModeWeights { Japanese = 1, Audio = 1, Blank = 0, Flash = 0 }));
    }

    [Fact]
    public void No_usable_weight_allocates_nothing()
    {
        var weights = new ModeWeights { Japanese = 0, Audio = 2, Blank = 0, Flash = 0 };

        Assert.Equal((0, 0, 0, 0), Allocate(10, weights, NoAudio));
    }

    [Theory]
    [InlineData(5, 3, 1, 1)]   // 新規3・苦手2 のうち1を再確認に
    [InlineData(10, 5, 4, 1)]  // v1.0 5.3 の例
    [InlineData(20, 10, 8, 2)]
    public void Default_slots_match_v1_0_tables(int count, int newWords, int weak, int review)
    {
        var s = DefaultSettings.Create();

        var slots = QuestionPlanner.PlanSlots(count, s.Practice, s.Mastery);

        Assert.Equal(new QuestionSlots(newWords, weak, review), slots);
        Assert.Equal(count, slots.Total);
    }

    [Fact]
    public void Changing_new_word_ratio_changes_slots()
    {
        var s = DefaultSettings.Create();

        var slots = QuestionPlanner.PlanSlots(10, s.Practice with { NewWordRatio = 0.3 }, s.Mastery);

        Assert.Equal(new QuestionSlots(3, 6, 1), slots);
    }

    [Fact]
    public void Weak_words_only_has_no_new_slots()
    {
        var s = DefaultSettings.Create();

        var slots = QuestionPlanner.PlanSlots(10, s.Practice with { WeakWordsOnly = true }, s.Mastery);

        Assert.Equal(new QuestionSlots(0, 9, 1), slots);
    }

    [Fact]
    public void Review_never_exceeds_the_weak_portion()
    {
        var s = DefaultSettings.Create();

        var slots = QuestionPlanner.PlanSlots(5, s.Practice with { NewWordRatio = 1.0 }, s.Mastery);

        Assert.Equal(new QuestionSlots(5, 0, 0), slots);
    }
}
