using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

namespace EikenSpelling.Core.Application;

/// <summary>Apply の結果。回答の入力文字列は含めない。</summary>
public sealed record AnswerResult(
    LearningProgress Progress,
    bool IsDuplicate,
    LearningState? StateBefore,
    LearningState? StateAfter,
    bool BecameMastered,
    bool ReturnedToWeak,
    bool ReviewAdvanced);

/// <summary>1回の回答を学習記録に反映する入口(design.md 4章)。元の記録は変えず、新しい記録を返す。</summary>
public static class AnswerRecorder
{
    public static AnswerResult Apply(LearningProgress progress, AnswerEvent answer, LearningSettings settings)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(settings);
        Validate(answer);

        var existing = progress.Word(answer.WordId);
        if (progress.AppliedAnswerIds.Contains(answer.Id))
        {
            return new(progress, true, existing?.State, existing?.State, false, false, false);
        }

        var isRetry = answer.Id.AttemptType == AttemptType.Retry;
        if (existing is null && isRetry)
        {
            throw new InvalidOperationException($"記録の無い単語 {answer.WordId} に追加再練習の回答が来ました。");
        }

        var word = RecordMode(existing ?? new WordProgress { WordId = answer.WordId }, answer);
        var after = isRetry ? word : ApplyNormal(word, existing is null, answer, settings);

        var updated = progress with
        {
            Words = progress.Words.SetItem(answer.WordId, after),
            AppliedAnswerIds = progress.AppliedAnswerIds.Add(answer.Id),
        };

        var before = existing?.State;
        return new(
            updated,
            false,
            before,
            after.State,
            BecameMastered: before != LearningState.Mastered && after.State == LearningState.Mastered,
            ReturnedToWeak: before == LearningState.Mastered && after.State == LearningState.Weak,
            ReviewAdvanced: existing is not null && before == LearningState.Mastered
                && after.State == LearningState.Mastered
                && (after.ReviewStage != existing.ReviewStage || after.NextReviewDate != existing.NextReviewDate));
    }

    private static void Validate(AnswerEvent answer)
    {
        if (string.IsNullOrWhiteSpace(answer.WordId))
        {
            throw new ArgumentException("単語IDが空です。", nameof(answer));
        }

        if (!Enum.IsDefined(answer.Mode) || !Enum.IsDefined(answer.Id.AttemptType))
        {
            throw new ArgumentException("出題モードまたは回答の種類が定義外です。", nameof(answer));
        }
    }

    /// <summary>モード別記録の更新(通常・再練習の両方。design.md 2章の表)。</summary>
    private static WordProgress RecordMode(WordProgress word, AnswerEvent answer)
    {
        var m = word.Mode(answer.Mode);
        var isRetry = answer.Id.AttemptType == AttemptType.Retry;
        m = (isRetry, answer.IsCorrect) switch
        {
            (false, true) => m with { NormalCorrect = m.NormalCorrect + 1 },
            (false, false) => m with { NormalWrong = m.NormalWrong + 1 },
            (true, true) => m with { RetryCorrect = m.RetryCorrect + 1 },
            (true, false) => m with { RetryWrong = m.RetryWrong + 1 },
        };

        if (!answer.IsCorrect && IsNewer(answer.AnsweredAt, m.LastWrongAt))
        {
            m = m with { LastWrongInput = answer.Input, LastWrongAt = answer.AnsweredAt };
        }

        if (IsNewer(answer.AnsweredAt, m.LastAskedAt))
        {
            m = m with { LastAskedAt = answer.AnsweredAt };
        }

        return word with { Modes = word.Modes.SetItem(answer.Mode, m) };
    }

    /// <summary>通常問題での状態の更新(design.md 4章 7)。</summary>
    private static WordProgress ApplyNormal(WordProgress word, bool isFirst, AnswerEvent answer, LearningSettings settings)
    {
        if (IsNewer(answer.AnsweredAt, word.LastNormalAskedAt))
        {
            word = word with { LastNormalAskedAt = answer.AnsweredAt };
        }

        var date = JstClock.ToJstDate(answer.AnsweredAt);
        if (!answer.IsCorrect)
        {
            var weak = word.State == LearningState.Mastered
                || word.NormalMistakes >= settings.WeakWordScore.MarkWeakAfterMistakes;
            return word with
            {
                State = weak ? LearningState.Weak : LearningState.Normal,
                MasteryStreak = 0,
                LastEligibleCorrectDate = null,
                ReviewStage = 0,
                NextReviewDate = null,
            };
        }

        if (isFirst)
        {
            return word with { State = LearningState.Normal };
        }

        return word.State switch
        {
            LearningState.Weak => MasteryEvaluator.ApplyCorrect(word, answer.Mode, date, answer.AnsweredAt, settings.Mastery),
            LearningState.Mastered when word.NextReviewDate is { } due && date >= due => Advance(word, date, settings.Mastery),
            _ => word,
        };
    }

    private static WordProgress Advance(WordProgress word, DateOnly date, MasterySettings mastery)
    {
        var (stage, next) = ReviewScheduler.Advance(word.ReviewStage, date, mastery);
        return word with { ReviewStage = stage, NextReviewDate = next };
    }

    private static bool IsNewer(DateTimeOffset value, DateTimeOffset? current) => current is null || value > current;
}
