namespace EikenSpelling.Core.Domain;

/// <summary>
/// 日付判定は日本時間(JST)で行う(仕様 12.3)。Core は現在時刻を直接読まず、TimeProvider から受け取る。
/// 日本には夏時間がないので、タイムゾーンIDの違い(Windows / Linux)を避けて固定の +9:00 を使う。
/// </summary>
public static class JstClock
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(9);

    public static DateTimeOffset Now(TimeProvider timeProvider) => timeProvider.GetUtcNow().ToOffset(Offset);

    public static DateOnly Today(TimeProvider timeProvider) => ToJstDate(timeProvider.GetUtcNow());

    public static DateOnly ToJstDate(DateTimeOffset moment) => DateOnly.FromDateTime(moment.ToOffset(Offset).DateTime);
}
