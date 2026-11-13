using System.Collections.Immutable;

using EikenSpelling.Core.Application;
using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Core.Tests;

/// <summary>progress.json の変換(design.md 3章・5章 Core)。</summary>
public class ProgressJsonTests
{
    private static ProgressDocument Sample()
    {
        var l = new Learner();
        l.MakeMastered("accommodate", Jst.Day0);
        l.Answer("tax", Blank, false, new DateTimeOffset(2026, 11, 3, 1, 2, 3, TimeSpan.Zero), input: "taks"); // Z の日時
        l.Answer("tax", Flash, true, Jst.At(Jst.Day0.AddDays(3)), AttemptType.Retry);
        return new ProgressDocument { WordSetVersion = "ws-1", SourceVersion = "ngsl-1.2", Progress = l.Progress };
    }

    [Fact]
    public void Round_trip_keeps_all_values()
    {
        var original = Sample();

        var restored = ProgressJson.Parse(ProgressJson.Serialize(original));

        Assert.Equal(original.SchemaVersion, restored.SchemaVersion);
        Assert.Equal("ws-1", restored.WordSetVersion);
        Assert.Equal("ngsl-1.2", restored.SourceVersion);
        Assert.Equal(original.Progress.AppliedAnswerIds.OrderBy(i => i.QuestionIndex), restored.Progress.AppliedAnswerIds.OrderBy(i => i.QuestionIndex));
        Assert.Equal(original.Progress.Words.Keys.Order(), restored.Progress.Words.Keys.Order());
        foreach (var (id, word) in original.Progress.Words)
        {
            var r = restored.Progress.Word(id)!;
            Assert.Equal(word with { Modes = r.Modes }, r); // Modes 以外の項目
            Assert.Equal(word.Modes.Keys.Order(), r.Modes.Keys.Order());
            foreach (var (mode, record) in word.Modes)
            {
                Assert.Equal(record, r.Modes[mode]);
            }
        }
    }

    [Fact]
    public void Offsets_are_kept_as_given()
    {
        var restored = ProgressJson.Parse(ProgressJson.Serialize(Sample()));

        Assert.Equal(TimeSpan.Zero, restored.Progress.Word("tax")!.Mode(Blank).LastWrongAt!.Value.Offset);
        Assert.Equal(TimeSpan.FromHours(9), restored.Progress.Word("tax")!.Mode(Flash).LastAskedAt!.Value.Offset);
    }

    [Fact]
    public void Word_keys_use_ordinal_comparer_after_parse()
    {
        var restored = ProgressJson.Parse(ProgressJson.Serialize(Sample()));

        Assert.Same(StringComparer.Ordinal, restored.Progress.Words.KeyComparer);
        Assert.Null(restored.Progress.Word("TAX"));
    }

    [Fact]
    public void Computed_properties_are_not_written()
    {
        var json = ProgressJson.Serialize(Sample());

        Assert.DoesNotContain("accuracy", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("normalMistakes", json, StringComparison.OrdinalIgnoreCase);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "\"schemaVersion\""));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("{ broken")]
    [InlineData("""{ "schemaVersion": "one" }""")]
    [InlineData("""{ "schemaVersion": 1, "progress": null }""")]
    [InlineData("""{ "schemaVersion": 1, "progress": { "words": { "tax": null } } }""")]
    [InlineData("""{ "schemaVersion": 1, "progress": { "words": { "tax": { "wordId": "tax", "modes": { "Japanese": null } } } } }""")]
    [InlineData("""{ "schemaVersion": 1, "progress": { "appliedAnswerIds": [null] } }""")]
    [InlineData("""{ "schemaVersion": 1, "wordSetVersion": null }""")]
    public void Broken_documents_are_invalid_data(string json)
    {
        Assert.Throws<InvalidDataException>(() => ProgressJson.Parse(json));
    }

    [Fact]
    public void Newer_schema_is_detected_before_full_parse()
    {
        const string json = """{ "schemaVersion": 2, "progress": { "words": "changed-shape" } }""";

        Assert.Equal(2, ProgressJson.ReadSchemaVersion(json));
        Assert.Throws<InvalidDataException>(() => ProgressJson.Parse(json));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "schemaVersion": null }""")]
    [InlineData("[1]")]
    public void Missing_or_invalid_schema_version_is_invalid_data(string json)
    {
        Assert.Throws<InvalidDataException>(() => ProgressJson.ReadSchemaVersion(json));
    }

    [Fact]
    public void Duplicate_schema_version_keys_that_disagree_are_invalid()
    {
        Assert.Throws<InvalidDataException>(() => ProgressJson.Parse("""{ "schemaVersion": 1, "SchemaVersion": 2, "progress": {} }"""));
    }

    [Fact]
    public void Schema_version_beyond_int_is_treated_as_newer()
    {
        Assert.True(ProgressJson.ReadSchemaVersion("""{ "schemaVersion": 99999999999 }""") > ProgressDocument.CurrentSchemaVersion);
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 1.5 }""")]
    [InlineData("""{ "schemaVersion": -1e20 }""")]
    public void Fractional_or_huge_negative_schema_version_is_invalid(string json)
    {
        Assert.Throws<InvalidDataException>(() => ProgressJson.ReadSchemaVersion(json));
    }

    [Fact]
    public void Settings_version_is_null_when_absent()
    {
        Assert.Null(LearningSettingsJson.ReadSettingsVersion("""{ "practice": { "questionCount": 20 } }"""));
        Assert.Equal(1, LearningSettingsJson.ReadSettingsVersion("""{ "settingsVersion": 1 }"""));
    }

    [Fact]
    public void Unknown_fields_are_ignored_and_missing_fields_get_defaults()
    {
        var doc = ProgressJson.Parse("""{ "schemaVersion": 1, "futureFeature": { "x": true }, "progress": {} }""");

        Assert.Equal("", doc.WordSetVersion);
        Assert.Empty(doc.Progress.Words);
        Assert.Empty(doc.Progress.AppliedAnswerIds);
    }
}

/// <summary>読み込んだ学習記録の検査(design.md 4章)。</summary>
public class ProgressValidatorTests
{
    private static readonly DateTimeOffset Now = Jst.At(new DateOnly(2026, 12, 1), 10);
    private static readonly LearningSettings Settings = DefaultSettings.Create();

    private static LearningProgress With(WordProgress word, string? key = null) =>
        LearningProgress.Empty with { Words = LearningProgress.Empty.Words.SetItem(key ?? word.WordId, word) };

    private static IReadOnlyList<ProgressIssue> Check(LearningProgress p, LearningSettings? s = null) =>
        ProgressValidator.Validate(p, s ?? Settings, Now);

    private static bool HasCritical(LearningProgress p, LearningSettings? s = null) =>
        Check(p, s).Any(i => i.Severity == ProgressIssueSeverity.Critical);

    private static bool HasWarning(LearningProgress p, LearningSettings? s = null) =>
        Check(p, s).Any(i => i.Severity == ProgressIssueSeverity.Warning);

    private static WordProgress Weak(int streak = 0) => new()
    {
        WordId = "tax", State = LearningState.Weak, MasteryStreak = streak,
        LastEligibleCorrectDate = streak > 0 ? new DateOnly(2026, 11, 30) : null,
    };

    private static WordProgress Mastered(int stage = 1) => new()
    {
        WordId = "tax", State = LearningState.Mastered, MasteryStreak = 2, ReviewStage = stage,
        LastEligibleCorrectDate = new DateOnly(2026, 11, 20), MasteredAt = Jst.At(new DateOnly(2026, 11, 20)),
        NextReviewDate = new DateOnly(2026, 11, 27),
    };

    [Fact]
    public void Records_made_by_answer_recorder_are_valid()
    {
        var l = new Learner();
        l.MakeMastered("a", Jst.Day0);
        l.MakeWeak("b", Jst.Day0);
        l.Answer("c", Japanese, true, Jst.At(Jst.Day0));

        Assert.Empty(Check(l.Progress));
    }

    [Fact]
    public void Key_and_word_id_mismatch_is_critical()
    {
        Assert.True(HasCritical(With(new WordProgress { WordId = "tax" }, key: "fax")));
    }

    [Fact]
    public void Empty_word_id_is_critical()
    {
        Assert.True(HasCritical(With(new WordProgress { WordId = "" }, key: "")));
    }

    [Fact]
    public void Undefined_state_or_mode_is_critical()
    {
        Assert.True(HasCritical(With(new WordProgress { WordId = "tax", State = (LearningState)9 })));
        var badMode = new WordProgress { WordId = "tax", Modes = ImmutableDictionary<LearningMode, ModeRecord>.Empty.Add((LearningMode)9, new ModeRecord()) };
        Assert.True(HasCritical(With(badMode)));
    }

    [Fact]
    public void Negative_values_are_critical()
    {
        var negCount = new WordProgress { WordId = "tax", Modes = ImmutableDictionary<LearningMode, ModeRecord>.Empty.Add(Japanese, new ModeRecord { RetryWrong = -1 }) };
        Assert.True(HasCritical(With(negCount)));
        Assert.True(HasCritical(With(Weak() with { MasteryStreak = -1 })));
        Assert.True(HasCritical(With(Mastered() with { ReviewStage = -1 })));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Normal_or_weak_must_not_have_review_schedule(int variant)
    {
        var bad = variant switch
        {
            1 => new WordProgress { WordId = "tax", State = LearningState.Normal, ReviewStage = 1 },
            2 => Weak() with { NextReviewDate = new DateOnly(2026, 12, 5) },
            _ => new WordProgress { WordId = "tax", State = LearningState.Normal, MasteryStreak = 1 },
        };

        Assert.True(HasCritical(With(bad)));
    }

    [Fact]
    public void Weak_with_streak_needs_last_eligible_date()
    {
        Assert.True(HasCritical(With(Weak(1) with { LastEligibleCorrectDate = null })));
    }

    [Fact]
    public void Mastered_needs_stage_mastered_at_and_eligible_date()
    {
        Assert.True(HasCritical(With(Mastered() with { ReviewStage = 0 })));
        Assert.True(HasCritical(With(Mastered() with { MasteredAt = null })));
        Assert.True(HasCritical(With(Mastered() with { LastEligibleCorrectDate = null })));
        Assert.False(HasCritical(With(Mastered() with { NextReviewDate = null }))); // 最後の段階を終えた
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    public void Answer_id_with_empty_session_or_negative_index_is_critical(int index, bool critical)
    {
        var p = LearningProgress.Empty with
        {
            AppliedAnswerIds = [new AnswerId(Guid.NewGuid(), index, AttemptType.Normal), new AnswerId(Guid.NewGuid(), 0, AttemptType.Normal)],
        };

        Assert.Equal(critical, HasCritical(p));
        Assert.True(HasCritical(LearningProgress.Empty with { AppliedAnswerIds = [new AnswerId(Guid.Empty, 0, AttemptType.Normal)] }));
    }

    [Fact]
    public void Future_date_is_warning_only_after_jst_midnight()
    {
        var today = JstClock.ToJstDate(Now);
        var todayDate = Weak(1) with { LastEligibleCorrectDate = today };
        var tomorrow = Weak(1) with { LastEligibleCorrectDate = today.AddDays(1) };

        Assert.Empty(Check(With(todayDate)));
        Assert.True(HasWarning(With(tomorrow)));
        Assert.False(HasCritical(With(tomorrow)));
    }

    [Fact]
    public void Jst_midnight_decides_today_and_tomorrow()
    {
        var d = new DateOnly(2026, 12, 1);
        var lastSecond = new DateTimeOffset(2026, 11, 30, 14, 59, 59, TimeSpan.Zero); // JST 11/30 23:59:59
        var midnight = new DateTimeOffset(2026, 11, 30, 15, 0, 0, TimeSpan.Zero);     // JST 12/1 0:00:00
        var p = With(Weak(1) with { LastEligibleCorrectDate = d });

        Assert.Contains(ProgressValidator.Validate(p, Settings, lastSecond), i => i.Severity == ProgressIssueSeverity.Warning); // まだ 11/30 → 12/1 は明日
        Assert.Empty(ProgressValidator.Validate(p, Settings, midnight));                                                       // 12/1 になった → 今日
    }

    [Fact]
    public void Critical_and_warning_together_contains_critical()
    {
        var p = With(Weak(1) with { LastEligibleCorrectDate = new DateOnly(2027, 1, 1), ReviewStage = 1 });

        var issues = Check(p);

        Assert.Contains(issues, i => i.Severity == ProgressIssueSeverity.Critical);
        Assert.Contains(issues, i => i.Severity == ProgressIssueSeverity.Warning);
    }

    [Fact]
    public void Future_timestamp_is_warning()
    {
        var w = new WordProgress { WordId = "tax", LastNormalAskedAt = Now.AddMinutes(1) };

        Assert.True(HasWarning(With(w)));
        Assert.Empty(Check(With(w with { LastNormalAskedAt = Now })));
    }

    [Theory]
    [InlineData(1, false)] // 必要回数−1
    [InlineData(2, true)]  // 必要回数(設定を下げた直後に起きうる)
    [InlineData(3, true)]
    public void Weak_streak_at_or_above_required_is_warning(int streak, bool warning)
    {
        var p = With(Weak(streak));

        Assert.Equal(warning, HasWarning(p));
        Assert.False(HasCritical(p));
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, false)] // 間隔の数
    [InlineData(4, true)]  // 間隔を減らした後に起きうる
    public void Stage_above_interval_count_is_warning(int stage, bool warning)
    {
        var p = With(Mastered(stage));

        Assert.Equal(warning, HasWarning(p));
        Assert.False(HasCritical(p));
    }

    [Fact]
    public void Stage_warning_follows_changed_settings()
    {
        var shortIntervals = Settings with { Mastery = Settings.Mastery with { ReviewIntervalsDays = [7, 14] } };

        Assert.True(HasWarning(With(Mastered(3)), shortIntervals));
    }

    [Fact]
    public void Too_long_stored_input_is_warning()
    {
        var w = new WordProgress
        {
            WordId = "tax",
            Modes = ImmutableDictionary<LearningMode, ModeRecord>.Empty.Add(Japanese, new ModeRecord { NormalWrong = 1, LastWrongInput = new string('x', InputRules.MaxStoredInputLength + 1) }),
        };

        Assert.True(HasWarning(With(w)));
        Assert.False(HasCritical(With(w)));
    }

    [Fact]
    public void Messages_do_not_contain_wrong_input()
    {
        var secret = new string('s', InputRules.MaxStoredInputLength + 1);
        var w = new WordProgress { WordId = "tax", Modes = ImmutableDictionary<LearningMode, ModeRecord>.Empty.Add(Japanese, new ModeRecord { LastWrongInput = secret }) };

        Assert.All(Check(With(w)), i => Assert.DoesNotContain("sss", i.Message));
    }
}

/// <summary>誤答入力の切り詰めと、回数の上限(design.md 2章)。</summary>
public class InputRulesAndCountTests
{
    [Theory]
    [InlineData(99, 99)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    public void Input_is_truncated_at_max(int length, int expected)
    {
        Assert.Equal(expected, InputRules.TruncateForStorage(new string('a', length))!.Length);
    }

    [Fact]
    public void Surrogate_pair_is_not_split()
    {
        var input = new string('a', InputRules.MaxStoredInputLength - 1) + "😀" + "b"; // 99文字目の次にサロゲートペア

        var result = InputRules.TruncateForStorage(input)!;

        Assert.Equal(InputRules.MaxStoredInputLength - 1, result.Length);
        Assert.False(char.IsHighSurrogate(result[^1]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Null_or_empty_is_kept(string? input)
    {
        Assert.Equal(input, InputRules.TruncateForStorage(input));
    }

    [Fact]
    public void Recorder_stores_truncated_input()
    {
        var l = new Learner();

        l.Answer("tax", Japanese, false, Jst.At(Jst.Day0), input: new string('x', 500));

        Assert.Equal(InputRules.MaxStoredInputLength, l.Word("tax").Mode(Japanese).LastWrongInput!.Length);
    }

    [Fact]
    public void Totals_do_not_overflow_with_huge_counts()
    {
        var r = new ModeRecord { NormalCorrect = int.MaxValue, NormalWrong = int.MaxValue };
        var w = new WordProgress
        {
            WordId = "tax",
            Modes = ImmutableDictionary<LearningMode, ModeRecord>.Empty.Add(Japanese, r).Add(Audio, r),
        };

        Assert.Equal(0.5, r.Accuracy);
        Assert.Equal(int.MaxValue, w.NormalMistakes);
    }

    [Fact]
    public void Counts_stop_at_int_max()
    {
        var l = new Learner();
        l.Answer("tax", Japanese, true, Jst.At(Jst.Day0));
        var w = l.Word("tax");
        l.Progress = l.Progress with
        {
            Words = l.Progress.Words.SetItem("tax", w with { Modes = w.Modes.SetItem(Japanese, w.Mode(Japanese) with { NormalCorrect = int.MaxValue }) }),
        };

        l.Answer("tax", Japanese, true, Jst.At(Jst.Day0.AddDays(1)));

        Assert.Equal(int.MaxValue, l.Word("tax").Mode(Japanese).NormalCorrect);
    }
}
