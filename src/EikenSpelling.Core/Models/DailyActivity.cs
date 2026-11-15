using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace EikenSpelling.Core.Models;

/// <summary>1日に採点した問題数(通常枠だけ。2026-11-15 本人確定の日別計上ルール)。</summary>
public sealed record DailyCount(DateOnly Date, int Graded);

/// <summary>
/// 日別の実績。加算は AnswerRecorder.Apply だけが行う(通常枠の採点を学習記録に反映するのと同じ更新で1回)。
/// RecordingSince はこの版で最初に数えた日。それより前の日は「記録なし」(旧データから推測しない)。
/// </summary>
public sealed record DailyActivity
{
    public static readonly DailyActivity Empty = new();

    /// <summary>1日の件数として受け付ける上限(学習ルールではなく、手で書き換えた値の桁あふれを防ぐ安全な範囲)。</summary>
    public const int MaxGradedPerDay = 1_000_000;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateOnly? RecordingSince { get; init; }

    /// <summary>日付の昇順。</summary>
    public ImmutableArray<DailyCount> Days { get; init; } = [];

    /// <summary>採点の時刻のオフセットの壁時計の日付(呼び出し側は端末のローカル時刻を渡す)。</summary>
    public static DateOnly DayOf(DateTimeOffset at) => DateOnly.FromDateTime(at.DateTime);

    /// <summary>
    /// その日に1問足した新しい値。最初の加算で記録開始日が入る。
    /// 記録開始日より前の日(時計を戻した・西のタイムゾーンへ移った)の採点も数え、記録開始日をその日へ下げる
    /// (開始日より前の日を残すと検査で壊れた記録になり、保存できなくなるため)。件数は上限で頭打ち。
    /// </summary>
    public DailyActivity Count(DateOnly day)
    {
        var existing = Days.FirstOrDefault(d => d.Date == day);
        var days = existing is null
            ? Days.Add(new DailyCount(day, 1)).Sort((x, y) => x.Date.CompareTo(y.Date))
            : Days.Replace(existing, existing with { Graded = Math.Min(existing.Graded, MaxGradedPerDay - 1) + 1 });
        var since = RecordingSince is { } s && s < day ? s : day;
        return this with { RecordingSince = since, Days = days };
    }

    /// <summary>その日の件数。記録開始前(または開始していない)なら null = 記録なし。</summary>
    public int? CountOn(DateOnly day)
    {
        if (RecordingSince is not { } since || day < since)
        {
            return null;
        }

        return Days.FirstOrDefault(d => d.Date == day)?.Graded ?? 0;
    }
}
