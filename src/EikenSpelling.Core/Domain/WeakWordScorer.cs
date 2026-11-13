using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

namespace EikenSpelling.Core.Domain;

/// <summary>苦手度(v1.1 7.2、2026-11-13 正式採用)。値は WeakWordScoreSettings と MasterySettings から取る。</summary>
public static class WeakWordScorer
{
    /// <summary>
    /// min(通常問題の誤答数, 上限) + 直近の誤答 + 克服まであと1回(今日すでに対象正答していれば無し)+ 最終通常出題から一定日数以上。
    /// 日数の差は日本時間の日付で数える。未来・極端な日時は今日とみなす。合計は long。
    /// </summary>
    public static long Score(WordProgress word, LearningSettings settings, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(word);
        ArgumentNullException.ThrowIfNull(settings);
        var s = settings.WeakWordScore;
        var today = JstClock.ToJstDate(now);
        long score = Math.Min(word.NormalMistakes, s.MistakeCap);

        if (LatestMistake.Of(word) is { } last && DaysBetween(last.At, today) < s.RecentMistakeDays)
        {
            score += s.RecentMistakeBonus;
        }

        if (IsOneStepFromMastery(word, settings, today))
        {
            score += s.PendingMasteryBonus;
        }

        if (word.LastNormalAskedAt is not { } asked || DaysBetween(asked, today) >= s.OverdueDays)
        {
            score += s.OverdueBonus;
        }

        return score;
    }

    /// <summary>苦手単語の出題順: 点数の高い順 → 今日まだ対象正答していないもの → 最終通常出題が古い順(無しが先)→ 単語ID 順。</summary>
    public static IReadOnlyList<WordProgress> Order(IEnumerable<WordProgress> weakWords, LearningSettings settings, DateTimeOffset now)
    {
        var today = JstClock.ToJstDate(now);
        return weakWords
            .Select(w => (Word: w, Score: Score(w, settings, now)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => MasteryRules.CountedToday(x.Word, today) ? 1 : 0)
            .ThenBy(x => x.Word.LastNormalAskedAt.HasValue ? 1 : 0)
            .ThenBy(x => x.Word.LastNormalAskedAt)
            .ThenBy(x => x.Word.WordId, StringComparer.Ordinal)
            .Select(x => x.Word)
            .ToList();
    }

    private static bool IsOneStepFromMastery(WordProgress word, LearningSettings settings, DateOnly today) =>
        word.MasteryStreak >= 1
        && word.MasteryStreak == settings.Mastery.RequiredCorrectDays - 1
        && !MasteryRules.CountedToday(word, today);

    /// <summary>日本時間の日付の差(今日 − その日)。未来・変換できない日時は 0。</summary>
    private static int DaysBetween(DateTimeOffset moment, DateOnly today)
    {
        if (JstClock.TryToJst(moment) is not { } jst)
        {
            return 0;
        }

        return Math.Max(0, today.DayNumber - DateOnly.FromDateTime(jst.DateTime).DayNumber);
    }
}
