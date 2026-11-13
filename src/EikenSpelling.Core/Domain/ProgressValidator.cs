using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

namespace EikenSpelling.Core.Domain;

public enum ProgressIssueSeverity
{
    /// <summary>読めるが知らせる。</summary>
    Warning,

    /// <summary>壊れているのと同じ扱い。</summary>
    Critical,
}

/// <summary>検査の問題。文言に誤答入力やファイルのパスは含めない。</summary>
public sealed record ProgressIssue(ProgressIssueSeverity Severity, string? WordId, string Code, string Message);

/// <summary>
/// 読み込んだ学習記録の検査(design.md 4章)。重大は状態の組み合わせの違反など、記録として成り立たないもの。
/// 警告は時計のずれや設定変更で起きうるもの。
/// </summary>
public static class ProgressValidator
{
    public static IReadOnlyList<ProgressIssue> Validate(LearningProgress progress, LearningSettings settings, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(settings);

        var issues = new List<ProgressIssue>();
        var today = JstClock.ToJstDate(now);
        void Critical(string? id, string code, string message) => issues.Add(new(ProgressIssueSeverity.Critical, id, code, message));
        void Warning(string? id, string code, string message) => issues.Add(new(ProgressIssueSeverity.Warning, id, code, message));

        foreach (var answerId in progress.AppliedAnswerIds)
        {
            if (answerId.SessionId == Guid.Empty || answerId.QuestionIndex < 0 || !Enum.IsDefined(answerId.AttemptType))
            {
                Critical(null, "answerId", "反映済みの回答IDに不正な値があります。");
                break;
            }
        }

        foreach (var (key, word) in progress.Words)
        {
            if (word is null)
            {
                Critical(key, "word.null", "単語の記録が空です。");
                continue;
            }

            var id = word.WordId;
            if (string.IsNullOrWhiteSpace(id) || !string.Equals(key, id, StringComparison.Ordinal))
            {
                Critical(key, "word.id", "単語IDが空、または記録の見出しと一致しません。");
            }

            if (!Enum.IsDefined(word.State))
            {
                Critical(id, "word.state", "学習状態の値が定義外です。");
            }

            foreach (var (mode, record) in word.Modes)
            {
                if (!Enum.IsDefined(mode) || record is null)
                {
                    Critical(id, "mode", "モード別の記録に定義外のモードか空の値があります。");
                    continue;
                }

                if (record.NormalCorrect < 0 || record.NormalWrong < 0 || record.RetryCorrect < 0 || record.RetryWrong < 0)
                {
                    Critical(id, "mode.count", "正答数・誤答数が負です。");
                }

                if (record.LastWrongInput is { Length: > InputRules.MaxStoredInputLength })
                {
                    Warning(id, "mode.input", "最後の誤答入力が上限の文字数を超えています。");
                }

                if (IsFuture(record.LastWrongAt, now) || IsFuture(record.LastAskedAt, now))
                {
                    Warning(id, "mode.future", "モード別の記録に現在より後の日時があります。");
                }
            }

            if (word.MasteryStreak < 0 || word.ReviewStage < 0)
            {
                Critical(id, "word.negative", "連続正答または再確認段階が負です。");
            }

            CheckStateCombination(word, Critical);

            if (IsFuture(word.LastNormalAskedAt, now) || IsFuture(word.MasteredAt, now)
                || word.LastEligibleCorrectDate > today)
            {
                Warning(id, "word.future", "現在より後の日時・日付があります(時計のずれ)。");
            }

            if (word.State == LearningState.Weak && word.MasteryStreak >= settings.Mastery.RequiredCorrectDays)
            {
                Warning(id, "word.streak", "苦手の単語の連続正答が今の必要回数以上です(次の対象正答で克服)。");
            }

            if (word.State == LearningState.Mastered && word.ReviewStage > settings.Mastery.ReviewIntervalsDays.Count)
            {
                Warning(id, "word.stage", "再確認段階が今の間隔の数より大きいです。");
            }
        }

        return issues;
    }

    /// <summary>状態ごとの正しい組み合わせ(learning-record の design.md 2章の表)。</summary>
    private static void CheckStateCombination(WordProgress word, Action<string?, string, string> critical)
    {
        var id = word.WordId;
        switch (word.State)
        {
            case LearningState.Normal:
                if (word.MasteryStreak != 0 || word.LastEligibleCorrectDate is not null
                    || word.ReviewStage != 0 || word.NextReviewDate is not null)
                {
                    critical(id, "state.normal", "通常の単語に克服・再確認の値があります。");
                }

                break;
            case LearningState.Weak:
                if (word.ReviewStage != 0 || word.NextReviewDate is not null
                    || (word.MasteryStreak > 0 && word.LastEligibleCorrectDate is null))
                {
                    critical(id, "state.weak", "苦手の単語の再確認・連続正答の値が成り立っていません。");
                }

                break;
            case LearningState.Mastered:
                if (word.ReviewStage < 1 || word.MasteredAt is null || word.LastEligibleCorrectDate is null)
                {
                    critical(id, "state.mastered", "克服済みの単語に段階・克服日時・対象正答日がありません。");
                }

                break;
        }
    }

    private static bool IsFuture(DateTimeOffset? value, DateTimeOffset now) => value is { } v && v > now;
}
