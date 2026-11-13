using EikenSpelling.Core.Application;
using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;

namespace EikenSpelling.Flow;

/// <summary>
/// 未完了の練習を今の単語データ・使えるモードで続けられるか(app-flow design 4.3)。
/// 答えようのない問題を誤答として記録しないため、残りの問題が1つでも表示できなければ再開させない。
/// </summary>
public static class ResumeCheck
{
    /// <summary>続けられるなら null、できなければ理由。</summary>
    public static string? Check(
        PracticeSession session, IReadOnlyDictionary<string, WordEntry> words, IReadOnlySet<LearningMode> availableModes)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(availableModes);

        foreach (var q in session.Questions.Skip(session.Position))
        {
            if (!words.TryGetValue(q.WordId, out var entry))
            {
                return "前回の練習の単語の一部が、今の単語データにありません。";
            }

            if (!ModeEligibility.For(entry, availableModes).Contains(q.Mode))
            {
                return "前回の練習の問題の一部を、今の環境では出題できません。";
            }
        }

        return null;
    }
}
