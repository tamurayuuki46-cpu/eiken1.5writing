using EikenSpelling.Core.Models;

namespace EikenSpelling.Core.Domain;

/// <summary>
/// 学習設定では変えない克服のルール(v1.2 3章「固定する仕様」の扱い)。
/// v0.1では克服対象モードを固定する(Q4: 日本語または音声モードでの自力正答)。
/// </summary>
public static class MasteryRules
{
    public static readonly IReadOnlySet<LearningMode> MasteryModes =
        new HashSet<LearningMode> { LearningMode.Japanese, LearningMode.Audio };

    public static bool CountsForMastery(LearningMode mode) => MasteryModes.Contains(mode);

    /// <summary>単語がいずれかの克服対象モードで出題できるか。</summary>
    public static bool HasMasteryMode(WordEntry word) =>
        (word.JapaneseEligible && CountsForMastery(LearningMode.Japanese))
        || (word.AudioEligible && CountsForMastery(LearningMode.Audio));

    /// <summary>今日(日本時間)すでに克服判定の対象として正答したか(同じ日の正答は連続正答に数えない)。</summary>
    public static bool CountedToday(WordProgress word, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(word);
        return word.LastEligibleCorrectDate == today;
    }
}
