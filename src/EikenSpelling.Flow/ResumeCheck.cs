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
    /// これから出うる問題: 列の未回答の分と、通常問題の途中なら、まだ列に足されていない再練習
    /// (回答済みの通常問題の誤答と、まだ答えていない通常問題が誤答になった場合。モードは SessionManager と同じ RetryModeRule で決まる)。
    /// </summary>
    private static IEnumerable<(string WordId, string Word, LearningMode Mode)> UpcomingQuestions(PracticeSession session)
    {
        var order = session.AnswerOrder;
        foreach (var i in order.Skip(session.Answers.Length))
        {
            var q = session.Questions[i];
            yield return (q.WordId, q.Word, q.Mode);
        }

        var practice = session.Settings.Practice;
        if (session.Answers.Length >= session.NormalCount || practice.RetryCount <= 0)
        {
            yield break; // 再練習はすでに列に入っている(または無い)
        }

        // 採点済みの通常問題は採点の順から引く(後回しがあると位置と番号がずれるため)
        var scored = new Dictionary<int, bool>();
        for (var k = 0; k < session.Answers.Length; k++)
        {
            scored[order[k]] = session.Answers[k].IsCorrect;
        }

        for (var i = 0; i < session.NormalCount; i++)
        {
            var q = session.Questions[i];
            var mayRetry = !scored.TryGetValue(i, out var correct) || !correct;
            if (mayRetry)
            {
                yield return (q.WordId, q.Word, RetryModeRule.Choose(q.Mode, q.EligibleModes, practice.AutoMix));
            }
        }
    }
}
