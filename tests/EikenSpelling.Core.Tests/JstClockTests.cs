using EikenSpelling.Core.Domain;

namespace EikenSpelling.Core.Tests;

/// <summary>NuGet の FakeTimeProvider を使わずに済むよう、最小限の固定時計を用意する。</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
}

public class JstClockTests
{
    [Fact]
    public void Utc_evening_is_next_day_in_japan()
    {
        // UTC 15:00 = JST 翌日 0:00
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 11, 13, 15, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 11, 14), JstClock.Today(clock));
    }

    [Fact]
    public void One_second_before_midnight_jst_is_still_the_same_day()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 11, 13, 14, 59, 59, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 11, 13), JstClock.Today(clock));
    }

    [Fact]
    public void Local_offset_of_the_input_does_not_matter()
    {
        // 同じ瞬間を別のオフセットで表しても、日本時間の日付は同じ
        var utc = new DateTimeOffset(2026, 11, 13, 15, 30, 0, TimeSpan.Zero);
        var newYork = utc.ToOffset(TimeSpan.FromHours(-5));

        Assert.Equal(JstClock.ToJstDate(utc), JstClock.ToJstDate(newYork));
    }
}
