using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

namespace EikenSpelling.Core.Domain;

/// <summary>苦手の単語の克服判定(v1.0 7.3)。通常問題の正答だけを渡す。</summary>
public static class MasteryEvaluator
{
    /// <summary>
    /// 克服対象モード(日本語・音声)で、最後の対象正答日より後の日(日本時間)の正答なら連続正答 +1。
    /// 同じ日・それより前の日・対象外モードの正答は何も変えない。必要回数以上で克服する。
    /// </summary>
    public static WordProgress ApplyCorrect(
        WordProgress word, LearningMode mode, DateOnly answeredDate, DateTimeOffset answeredAt, MasterySettings settings)
    {
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
            MasteredAt = answeredAt,
            ReviewStage = stage,
            NextReviewDate = next,
        };
    }
}
