using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

namespace EikenSpelling.Core.Tests;

/// <summary>3体レビュー(2026-11-13)の P1 対応: 壊れた・不正な settings.json を安全に扱う。</summary>
public class SettingsJsonRobustnessTests
{
    [Theory]
    [InlineData("""{ "practice": null }""")]
    [InlineData("""{ "practice": { "modeWeights": null } }""")]
    [InlineData("""{ "practice": { "questionCountChoices": null } }""")]
    [InlineData("""{ "practice": { "flashSecondsChoices": null } }""")]
    [InlineData("""{ "mastery": null }""")]
    [InlineData("""{ "mastery": { "reviewLimits": null } }""")]
    [InlineData("""{ "mastery": { "reviewLimits": [null] } }""")]
    [InlineData("""{ "mastery": { "reviewIntervalsDays": null } }""")]
    [InlineData("""{ "weakWordScore": null }""")]
    [InlineData("""{ "vocabulary": null }""")]
    [InlineData("""{ "vocabulary": { "enabledSources": null } }""")]
    [InlineData("""{ "vocabulary": { "enabledSources": ["NGSL", null] } }""")]
    public void Explicit_null_is_reported_as_invalid_data(string json)
    {
        Assert.Throws<InvalidDataException>(() => LearningSettingsJson.Parse(json));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("""{ "practice": { "questionCount": "ten" } }""")]
    [InlineData("""{ "practice": { "questionCount": 2147483648 } }""")]
    [InlineData("""{ "practice": { "manualMode": "Foo" } }""")]
    public void Unreadable_values_are_reported_as_invalid_data(string json)
    {
        Assert.Throws<InvalidDataException>(() => LearningSettingsJson.Parse(json));
    }

    [Fact]
    public void Numeric_enum_outside_definition_is_reported_by_validator()
    {
        var s = LearningSettingsJson.Parse("""{ "practice": { "autoMix": true, "manualMode": 99 } }""");

        Assert.Contains("practice.manualMode", SettingsValidator.Validate(s).Select(i => i.Path));
    }
}

public class SettingsLimitsTests
{
    private static IEnumerable<string> Paths(LearningSettings s) => SettingsValidator.Validate(s).Select(i => i.Path);

    private static LearningSettings WithPractice(Func<PracticeSettings, PracticeSettings> change)
    {
        var s = DefaultSettings.Create();
        return s with { Practice = change(s.Practice) };
    }

    private static LearningSettings WithMastery(Func<MasterySettings, MasterySettings> change)
    {
        var s = DefaultSettings.Create();
        return s with { Mastery = change(s.Mastery) };
    }

    [Fact]
    public void Limits_match_decisions()
    {
        Assert.Equal(100, SettingsLimits.MaxQuestionCount);
        Assert.Equal(100, SettingsLimits.MaxModeWeight);
        Assert.Equal(3, SettingsLimits.MaxRetryCount);
        Assert.Equal(0.1, SettingsLimits.MinFlashSeconds);
        Assert.Equal(10.0, SettingsLimits.MaxFlashSeconds);
        Assert.Equal(3650, SettingsLimits.MaxReviewIntervalDays);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void Question_count_is_between_1_and_max(int count, bool valid)
    {
        Assert.Equal(valid, !Paths(WithPractice(p => p with { QuestionCount = count })).Contains("practice.questionCount"));
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void Question_count_choices_have_upper_limit(int choice, bool valid)
    {
        var paths = Paths(WithPractice(p => p with { QuestionCountChoices = [5, choice] }));

        Assert.Equal(valid, !paths.Contains("practice.questionCountChoices"));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void Mode_weight_is_between_0_and_max(int weight, bool valid)
    {
        var paths = Paths(WithPractice(p => p with { ModeWeights = new ModeWeights { Flash = weight } }));

        Assert.Equal(valid, !paths.Contains("practice.modeWeights.flash"));
    }

    [Fact]
    public void Huge_weights_are_reported_without_overflow()
    {
        var s = WithPractice(p => p with { ModeWeights = new ModeWeights { Japanese = 2_000_000_000, Audio = 2_000_000_000 } });

        var paths = Paths(s).ToList();

        Assert.Contains("practice.modeWeights.japanese", paths);
        Assert.Contains("practice.modeWeights.audio", paths);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void Retry_count_is_between_0_and_max(int count, bool valid)
    {
        Assert.Equal(valid, !Paths(WithPractice(p => p with { RetryCount = count })).Contains("practice.retryCount"));
    }

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(0.09, false)]
    [InlineData(0.1, true)]
    [InlineData(10.0, true)]
    [InlineData(10.01, false)]
    [InlineData(1e308, false)]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, false)]
    public void Flash_seconds_is_between_min_and_max(double seconds, bool valid)
    {
        Assert.Equal(valid, !Paths(WithPractice(p => p with { FlashSeconds = seconds })).Contains("practice.flashSeconds"));
    }

    [Theory]
    [InlineData(0.1, true)]
    [InlineData(0.09, false)]
    [InlineData(10.01, false)]
    public void Flash_seconds_choices_use_same_limits(double seconds, bool valid)
    {
        var paths = Paths(WithPractice(p => p with { FlashSecondsChoices = [1.0, seconds] }));

        Assert.Equal(valid, !paths.Contains("practice.flashSecondsChoices"));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(3650, true)]
    [InlineData(3651, false)]
    public void Review_interval_is_between_1_and_max(int days, bool valid)
    {
        var paths = Paths(WithMastery(m => m with { ReviewIntervalsDays = [7, days] }));

        Assert.Equal(valid, !paths.Contains("mastery.reviewIntervalsDays"));
    }
}

public class SettingsValidatorMoreRulesTests
{
    private static IEnumerable<string> Paths(LearningSettings s, IReadOnlySet<LearningMode>? modes = null) =>
        SettingsValidator.Validate(s, modes).Select(i => i.Path);

    [Fact]
    public void Newer_settings_version_is_reported()
    {
        Assert.Contains("settingsVersion", Paths(DefaultSettings.Create() with { SettingsVersion = LearningSettings.CurrentVersion + 1 }));
        Assert.Empty(Paths(DefaultSettings.Create() with { SettingsVersion = LearningSettings.CurrentVersion }));
    }

    [Fact]
    public void Empty_review_limits_is_reported()
    {
        var s = DefaultSettings.Create() with { Mastery = new MasterySettings { ReviewLimits = [] } };

        Assert.Contains("mastery.reviewLimits", Paths(s));
    }

    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, true)]
    [InlineData(1, -1, false)]
    public void Review_limit_values_are_checked(int upTo, int limit, bool valid)
    {
        var s = DefaultSettings.Create() with { Mastery = new MasterySettings { ReviewLimits = [new(upTo, limit)] } };

        Assert.Equal(valid, !Paths(s).Contains("mastery.reviewLimits[0]"));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void Required_correct_days_is_at_least_1(int days, bool valid)
    {
        var s = DefaultSettings.Create() with { Mastery = new MasterySettings { RequiredCorrectDays = days } };

        Assert.Equal(valid, !Paths(s).Contains("mastery.requiredCorrectDays"));
    }

    [Fact]
    public void Weak_word_score_values_are_checked()
    {
        var s = DefaultSettings.Create() with
        {
            WeakWordScore = new WeakWordScoreSettings { MarkWeakAfterMistakes = 0, MistakeCap = 0, OverdueBonus = -1 },
        };

        var paths = Paths(s).ToList();

        Assert.Contains("weakWordScore.markWeakAfterMistakes", paths);
        Assert.Contains("weakWordScore.mistakeCap", paths);
        Assert.Contains("weakWordScore", paths);
    }

    [Fact]
    public void Available_manual_mode_is_not_reported()
    {
        var s = DefaultSettings.Create() with { Practice = new PracticeSettings { AutoMix = false, ManualMode = LearningMode.Blank } };
        var withoutAudio = new HashSet<LearningMode> { LearningMode.Japanese, LearningMode.Blank, LearningMode.Flash };

        Assert.Empty(Paths(s, withoutAudio));
    }

    [Fact]
    public void Undefined_enum_values_are_reported()
    {
        var s = DefaultSettings.Create() with
        {
            Practice = new PracticeSettings { ManualMode = (LearningMode)99, RetryTiming = (RetryTiming)99 },
            Vocabulary = new VocabularySettings { Ordering = (NewWordOrdering)99 },
        };

        var paths = Paths(s).ToList();

        Assert.Contains("practice.manualMode", paths);
        Assert.Contains("practice.retryTiming", paths);
        Assert.Contains("vocabulary.ordering", paths);
    }
}
