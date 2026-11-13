using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

namespace EikenSpelling.Core.Domain;

/// <summary>苦手の単語の克服判定(v1.0 7.3)。通常問題の正答だけを渡す。</summary>
public static class MasteryEvaluator
{
    /// <summary>
    /// 克服対象モード(日本語・音声)で、最後の対象正答日より後の日(日本時間)の正答なら連続正答 +1。
    /// 同じ日・それより前の日・対象外モードの正答は何も変えない。必要回数以上で克服し、
    /// 連続正答は必要回数にそろえる(設定を下げた直後でも v1.1 10.3 の「2 = 克服条件達成」と同じ形にする)。
    /// </summary>
    public static WordProgress ApplyCorrect(
        WordProgress word, LearningMode mode, DateTimeOffset answeredAt, MasterySettings settings)
    {
        var answeredDate = JstClock.ToJstDate(answeredAt);
        if (word.State != LearningState.Weak
            || !MasteryRules.CountsForMastery(mode)
            || (word.LastEligibleCorrectDate is { } last && answeredDate <= last))
        {
            return word;
        }

        var counted = word with { MasteryStreak = word.MasteryStreak + 1, LastEligibleCorrectDate = answeredDate };
        if (counted.MasteryStreak < settings.RequiredCorrectDays)
        {
            return counted;
        }

        var (stage, next) = ReviewScheduler.FirstReview(answeredDate, settings);
        return counted with
        {
            State = LearningState.Mastered,
            MasteryStreak = settings.RequiredCorrectDays,
            MasteredAt = answeredAt,
            ReviewStage = stage,
            NextReviewDate = next,
        };
    }
}
