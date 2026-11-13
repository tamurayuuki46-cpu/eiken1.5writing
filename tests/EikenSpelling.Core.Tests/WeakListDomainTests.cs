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
    public void Min_value_moves_forward_and_converts()
    {
        Assert.Equal(DateTimeOffset.MinValue, JstClock.TryToJst(DateTimeOffset.MinValue));
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
