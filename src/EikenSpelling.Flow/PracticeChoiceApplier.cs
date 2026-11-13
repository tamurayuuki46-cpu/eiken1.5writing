using EikenSpelling.Core.Settings;

namespace EikenSpelling.Flow;

/// <summary>練習の選択を設定に重ねる。検査は QuestionSelector・SessionManager が行う(ここでは重ねるだけ)。</summary>
internal static class PracticeChoiceApplier
{
    public static LearningSettings Apply(LearningSettings settings, PracticeChoice choice)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(choice);
        return settings with
        {
            Practice = settings.Practice with
            {
                AutoMix = choice.AutoMix,
                ManualMode = choice.ManualMode,
                QuestionCount = choice.QuestionCount,
                WeakWordsOnly = choice.WeakWordsOnly,
                FlashSeconds = choice.FlashSeconds,
            },
        };
    }
}
