using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

namespace EikenSpelling.Core.Domain;

/// <summary>克服済み単語の定期再確認(v1.0 8章)。段階は 1〜間隔の数、予定日は日本時間の日付。</summary>
public static class ReviewScheduler
{
    /// <summary>克服したときの段階と初回予定日。</summary>
    public static (int Stage, DateOnly? NextReviewDate) FirstReview(DateOnly answeredDate, MasterySettings settings) =>
        settings.ReviewIntervalsDays.Count == 0
            ? (1, null)
            : (1, AddDaysCapped(answeredDate, settings.ReviewIntervalsDays[0]));

    /// <summary>
    /// 期限日以降に正答したときの次の段階と予定日。起点は実際の回答日。
    /// 最後の段階では RepeatLastInterval なら同じ間隔を繰り返し、そうでなければ予定日を無くす。
    /// 段階が範囲外(設定で間隔を減らした・手で書き換えた)でも 1〜間隔の数に収めて扱う。
    /// </summary>
    public static (int Stage, DateOnly? NextReviewDate) Advance(int currentStage, DateOnly answeredDate, MasterySettings settings)
    {
        var intervals = settings.ReviewIntervalsDays;
        if (intervals.Count == 0)
        {
            return (Math.Max(1, currentStage), null);
        }

        var stage = Math.Clamp(currentStage, 1, intervals.Count);
        if (stage == intervals.Count)
        {
            return settings.RepeatLastInterval
                ? (stage, AddDaysCapped(answeredDate, intervals[^1]))
                : (stage, null);
        }

        var next = stage + 1;
        return (next, AddDaysCapped(answeredDate, intervals[next - 1]));
    }

    /// <summary>再確認の期限が来ているか(=「再確認待ち」)。</summary>
    public static bool IsDue(WordProgress word, DateOnly today) =>
        word.State == LearningState.Mastered && word.NextReviewDate is { } next && next <= today;

    /// <summary>期限が来た再確認対象の単語ID。予定日の古い順、同じ日は単語ID順(v1.0 8.3)。</summary>
    public static IReadOnlyList<string> DueWordIds(LearningProgress progress, DateOnly today) =>
        progress.Words.Values
            .Where(w => IsDue(w, today))
            .OrderBy(w => w.NextReviewDate)
            .ThenBy(w => w.WordId, StringComparer.Ordinal)
            .Select(w => w.WordId)
            .ToList();

    private static DateOnly AddDaysCapped(DateOnly date, int days) =>
        days >= DateOnly.MaxValue.DayNumber - date.DayNumber ? DateOnly.MaxValue : date.AddDays(days);
}
