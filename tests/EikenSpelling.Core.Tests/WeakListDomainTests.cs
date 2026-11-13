using System.Collections.Immutable;

using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;

namespace EikenSpelling.Core.Tests;

public class JstClockTryToJstTests
{
    [Fact]
    public void Utc_moment_is_shown_in_japan_time()
    {
        var jst = JstClock.TryToJst(new DateTimeOffset(2026, 11, 1, 15, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 11, 2, 0, 0, 0, JstClock.Offset), jst);
        Assert.Equal(JstClock.Offset, jst!.Value.Offset);
    }

    [Fact]
    public void Moment_beyond_max_after_conversion_is_null()
    {
        Assert.Null(JstClock.TryToJst(DateTimeOffset.MaxValue)); // +9 時間で範囲を超える
    }

    [Fact]
    public void Last_convertible_moment_is_nine_hours_before_max()
    {
        var last = DateTimeOffset.MaxValue.AddHours(-9);

        Assert.NotNull(JstClock.TryToJst(last));
        Assert.Null(JstClock.TryToJst(last.AddTicks(1)));
    }

    [Fact]
    public void Min_value_moves_forward_and_converts()
    {
        Assert.Equal(DateTimeOffset.MinValue, JstClock.TryToJst(DateTimeOffset.MinValue));
    }
}

public class LatestMistakeTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 1, 10, 0, 0, TimeSpan.FromHours(9));

    [Fact]
    public void No_mistakes_is_null()
    {
        Assert.Null(LatestMistake.Of(new WordProgress { WordId = "a" }));
    }

    [Fact]
    public void Latest_moment_wins_and_same_moment_uses_mode_order()
    {
        var word = new WordProgress
        {
            WordId = "a",
            Modes = ImmutableDictionary<LearningMode, ModeRecord>.Empty
                .Add(LearningMode.Flash, new ModeRecord { LastWrongAt = At.AddMinutes(1), LastWrongInput = "f" })
                .Add(LearningMode.Blank, new ModeRecord { LastWrongAt = At.AddMinutes(1).ToUniversalTime(), LastWrongInput = "b" })
                .Add(LearningMode.Japanese, new ModeRecord { LastWrongAt = At, LastWrongInput = "j" }),
        };

        Assert.Equal(new LatestMistake(LearningMode.Blank, At.AddMinutes(1).ToUniversalTime(), "b"), LatestMistake.Of(word));
    }
}

public class MasteryRulesCountedTodayTests
{
    private static readonly DateOnly Today = new(2026, 11, 2);

    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, false)]
    [InlineData(1, false)]
    public void Counted_today_only_when_last_eligible_correct_date_is_today(int offsetDays, bool expected)
    {
        var word = new WordProgress { WordId = "a", LastEligibleCorrectDate = Today.AddDays(offsetDays) };

        Assert.Equal(expected, MasteryRules.CountedToday(word, Today));
    }

    [Fact]
    public void Never_counted_is_false()
    {
        Assert.False(MasteryRules.CountedToday(new WordProgress { WordId = "a" }, Today));
    }
}
