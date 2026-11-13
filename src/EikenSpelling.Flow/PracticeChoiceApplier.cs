using EikenSpelling.Core.Settings;

namespace EikenSpelling.Flow;

/// <summary>練習の選択を設定に重ねる。検査は QuestionSelector・SessionManager が行う(ここでは重ねるだけ)。</summary>
internal static class PracticeChoiceApplier
{
    /// <summary>基本設定の初期値(DefaultSettings の5項目)。</summary>
    public static PracticeChoice Defaults { get; } = FromSettings(DefaultSettings.Create());

    /// <summary>設定から基本設定の5項目を取り出す(Apply の逆)。</summary>
    public static PracticeChoice FromSettings(LearningSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var p = settings.Practice;
        return new PracticeChoice(p.AutoMix, p.ManualMode, p.QuestionCount, p.WeakWordsOnly, p.FlashSeconds);
    }

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
