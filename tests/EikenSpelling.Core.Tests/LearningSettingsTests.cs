using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

namespace EikenSpelling.Core.Tests;

public class LearningSettingsTests
{
    [Fact]
    public void Defaults_match_v1_1_decisions()
    {
        var s = DefaultSettings.Create();

        Assert.Equal(10, s.Practice.QuestionCount);
        Assert.Equal([5, 10, 20], s.Practice.QuestionCountChoices);
        Assert.Equal(0.5, s.Practice.NewWordRatio);
        Assert.Equal((3, 3, 2, 2), (s.Practice.ModeWeights.Japanese, s.Practice.ModeWeights.Audio,
            s.Practice.ModeWeights.Blank, s.Practice.ModeWeights.Flash));
        Assert.Equal(1.0, s.Practice.FlashSeconds);
        Assert.Equal(1, s.Practice.RetryCount);
        Assert.Equal(2, s.Mastery.RequiredCorrectDays);
        Assert.Equal([7, 14, 30], s.Mastery.ReviewIntervalsDays);
        Assert.True(s.Mastery.RepeatLastInterval);
        Assert.Equal(1, s.WeakWordScore.MarkWeakAfterMistakes);
        Assert.Equal(["NGSL", "NAWL", "BSL"], s.Vocabulary.EnabledSources);
        Assert.Empty(SettingsValidator.Validate(s));
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(10, 1)]
    [InlineData(20, 2)]
    [InlineData(3, 1)]   // 区切りより小さい
    [InlineData(15, 2)]  // 区切りの間
    [InlineData(50, 2)]  // 最後の区切りより大きい
    public void Review_limit_follows_thresholds(int questionCount, int expected)
    {
        Assert.Equal(expected, DefaultSettings.Create().Mastery.ReviewLimitFor(questionCount));
    }

    [Fact]
    public void Json_round_trip_keeps_values()
    {
        var original = DefaultSettings.Create() with
        {
            Practice = new PracticeSettings
            {
                QuestionCount = 20,
                AutoMix = false,
                ManualMode = LearningMode.Flash,
                FlashSeconds = 0.5,
                ModeWeights = new ModeWeights { Japanese = 4 },
            },
        };

        var restored = LearningSettingsJson.Parse(LearningSettingsJson.Serialize(original));

        Assert.Equal(20, restored.Practice.QuestionCount);
        Assert.False(restored.Practice.AutoMix);
        Assert.Equal(LearningMode.Flash, restored.Practice.ManualMode);
        Assert.Equal(0.5, restored.Practice.FlashSeconds);
        Assert.Equal(4, restored.Practice.ModeWeights.Japanese);
        Assert.Equal(3, restored.Practice.ModeWeights.Audio);
        Assert.Equal(original.Mastery.ReviewLimits, restored.Mastery.ReviewLimits);
    }

    [Fact]
    public void Missing_fields_get_defaults_and_unknown_fields_are_ignored()
    {
        const string json = """
        {
          "settingsVersion": 1,
          "practice": { "questionCount": 5, "modeWeights": { "flash": 0 } },
          "futureFeature": { "x": 1 }
        }
        """;

        var s = LearningSettingsJson.Parse(json);

        Assert.Equal(5, s.Practice.QuestionCount);
        Assert.Equal(0, s.Practice.ModeWeights.Flash);
        Assert.Equal(3, s.Practice.ModeWeights.Japanese); // 欠けたキーは0ではなく初期値
        Assert.Equal(2, s.Mastery.RequiredCorrectDays);
        Assert.Empty(SettingsValidator.Validate(s));
    }

    [Fact]
    public void Broken_json_is_reported_as_invalid_data()
    {
        Assert.Throws<InvalidDataException>(() => LearningSettingsJson.Parse("{ practice: "));
    }

    [Fact]
    public void Enums_are_written_as_strings()
    {
        var json = LearningSettingsJson.Serialize(DefaultSettings.Create());

        Assert.Contains("\"manualMode\": \"Japanese\"", json);
        Assert.Contains("\"retryTiming\": \"AfterNormalQuestions\"", json);
    }
}

public class SettingsValidatorTests
{
    private static LearningSettings WithPractice(Func<PracticeSettings, PracticeSettings> change)
    {
        var s = DefaultSettings.Create();
        return s with { Practice = change(s.Practice) };
    }

    private static IEnumerable<string> Paths(LearningSettings s, IReadOnlySet<LearningMode>? modes = null, WordSet? words = null) =>
        SettingsValidator.Validate(s, modes, words).Select(i => i.Path);

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(1.0, true)]
    [InlineData(-0.01, false)]
    [InlineData(1.01, false)]
    [InlineData(double.NaN, false)]
    public void New_word_ratio_must_be_between_0_and_1(double ratio, bool valid)
    {
        var paths = Paths(WithPractice(p => p with { NewWordRatio = ratio }));

        Assert.Equal(valid, !paths.Contains("practice.newWordRatio"));
    }

    [Fact]
    public void Question_count_must_be_positive()
    {
        Assert.Contains("practice.questionCount", Paths(WithPractice(p => p with { QuestionCount = 0 })));
    }

    [Fact]
    public void Negative_weight_is_invalid()
    {
        var s = WithPractice(p => p with { ModeWeights = new ModeWeights { Blank = -1 } });

        Assert.Contains("practice.modeWeights.blank", Paths(s));
    }

    [Fact]
    public void All_zero_weights_cannot_auto_mix()
    {
        var s = WithPractice(p => p with { ModeWeights = new ModeWeights { Japanese = 0, Audio = 0, Blank = 0, Flash = 0 } });

        Assert.Contains("practice.modeWeights", Paths(s));
    }

    [Fact]
    public void Weights_of_only_unavailable_modes_cannot_auto_mix()
    {
        var s = WithPractice(p => p with { ModeWeights = new ModeWeights { Japanese = 0, Audio = 5, Blank = 0, Flash = 0 } });
        var withoutAudio = new HashSet<LearningMode> { LearningMode.Japanese, LearningMode.Blank, LearningMode.Flash };

        Assert.Empty(Paths(s));
        Assert.Contains("practice.modeWeights", Paths(s, withoutAudio));
    }

    [Fact]
    public void Manual_mode_must_be_available()
    {
        var s = WithPractice(p => p with { AutoMix = false, ManualMode = LearningMode.Audio });
        var withoutAudio = new HashSet<LearningMode> { LearningMode.Japanese, LearningMode.Blank, LearningMode.Flash };

        Assert.Contains("practice.manualMode", Paths(s, withoutAudio));
    }

    [Fact]
    public void Mastery_values_are_checked()
    {
        var s = DefaultSettings.Create() with
        {
            Mastery = new MasterySettings
            {
                RequiredCorrectDays = 0,
                ReviewIntervalsDays = [7, 0],
                ReviewLimits = [new(10, 1), new(5, 1)],
            },
        };

        var paths = Paths(s).ToList();

        Assert.Contains("mastery.requiredCorrectDays", paths);
        Assert.Contains("mastery.reviewIntervalsDays", paths);
        Assert.Contains("mastery.reviewLimits[1]", paths);
    }

    [Fact]
    public void Retry_count_must_not_be_negative()
    {
        Assert.Contains("practice.retryCount", Paths(WithPractice(p => p with { RetryCount = -1 })));
    }

    [Fact]
    public void Enabled_sources_must_contain_words()
    {
        var words = new WordSet { Words = [new WordEntry { Id = "tax", Word = "tax", Ja = "税金", Sources = ["NGSL"] }] };
        var onlyBsl = DefaultSettings.Create() with { Vocabulary = new VocabularySettings { EnabledSources = ["BSL"] } };
        var none = DefaultSettings.Create() with { Vocabulary = new VocabularySettings { EnabledSources = [] } };

        Assert.Empty(Paths(DefaultSettings.Create(), words: words));
        Assert.Contains("vocabulary.enabledSources", Paths(onlyBsl, words: words));
        Assert.Contains("vocabulary.enabledSources", Paths(none));
    }
}
