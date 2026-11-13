using EikenSpelling.Core.Application;
using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;

namespace EikenSpelling.Flow;

/// <summary>
/// 未完了の練習を今の単語データ・使えるモードで続けられるか(app-flow design 4.3)。
/// 答えようのない問題を誤答として記録しないため、これから出る問題が1つでも表示できなければ再開させない。
/// </summary>
internal static class ResumeCheck
{
    /// <summary>続けられるなら null、できなければ理由。</summary>
    public static string? Check(
        PracticeSession session, IReadOnlyDictionary<string, WordEntry> words, IReadOnlySet<LearningMode> availableModes)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(availableModes);

        foreach (var (wordId, word, mode) in UpcomingQuestions(session))
        {
            if (!words.TryGetValue(wordId, out var entry) || !string.Equals(entry.Word, word, StringComparison.Ordinal))
            {
                return "前回の練習の単語の一部が、今の単語データにありません。";
            }

            if (!ModeEligibility.For(entry, availableModes).Contains(mode))
            {
                return "前回の練習の問題の一部を、今の環境では出題できません。";
            }
        }

        return null;
    }

    /// <summary>
    /// これから出る問題: 列の未回答の分と、通常問題の途中ならまだ列に足されていない再練習
    /// (回答済みの通常問題の誤答。モードは SessionManager と同じ RetryModeRule で決まる)。
    /// </summary>
    private static IEnumerable<(string WordId, string Word, LearningMode Mode)> UpcomingQuestions(PracticeSession session)
    {
        foreach (var q in session.Questions.Skip(session.Position))
        {
            yield return (q.WordId, q.Word, q.Mode);
        }

        var practice = session.Settings.Practice;
        if (session.Position >= session.NormalCount || practice.RetryCount <= 0)
        {
            yield break; // 再練習はすでに列に入っている(または無い)
        }

        for (var i = 0; i < session.Position; i++)
        {
            var q = session.Questions[i];
            if (q.Attempt == AttemptType.Normal && !session.Answers[i].IsCorrect)
            {
                yield return (q.WordId, q.Word, RetryModeRule.Choose(q.Mode, q.EligibleModes, practice.AutoMix));
            }
        }
    }
}
