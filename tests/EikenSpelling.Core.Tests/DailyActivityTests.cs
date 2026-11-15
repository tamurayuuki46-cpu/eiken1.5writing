using System.Collections.Immutable;

using EikenSpelling.Core.Application;
using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Core.Tests;

/// <summary>
/// 日別の計上(ロードマップ④、2026-11-15 本人確定): 通常枠の採点だけを1回数える。
/// 日付は採点の時刻のオフセットの壁時計(Flow は端末のローカル時刻を渡す)。
/// </summary>
public class DailyActivityTests
{
    private static readonly DateOnly Day = Jst.Day0;

    private static int? On(LearningProgress progress, DateOnly day) => progress.Daily.CountOn(day);

    [Fact]
    public void Normal_correct_and_wrong_answers_each_count_once_on_their_day()
    {
        var learner = new Learner();
        learner.Answer("a", Japanese, true, Jst.At(Day, 10));
        learner.Answer("b", Japanese, false, Jst.At(Day, 11));

        Assert.Equal(2, On(learner.Progress, Day));
        Assert.Equal(Day, learner.Progress.Daily.RecordingSince);
    }

    [Fact]
    public void Retry_answers_do_not_count()
    {
        var learner = new Learner();
        learner.Answer("a", Japanese, false, Jst.At(Day, 10));
        learner.Answer("a", Japanese, true, Jst.At(Day, 11), AttemptType.Retry);

        Assert.Equal(1, On(learner.Progress, Day));
    }

    [Fact]
    public void Duplicate_answer_id_does_not_count_again()
    {
        var learner = new Learner();
        var answer = new AnswerEvent(new AnswerId(learner.Session, 0, AttemptType.Normal), "a", Japanese, true, "a", Jst.At(Day, 10));
        learner.Submit(answer);
        var duplicate = learner.Submit(answer);

        Assert.True(duplicate.IsDuplicate);
        Assert.Equal(1, On(learner.Progress, Day));
    }

    [Fact]
    public void Day_boundary_follows_the_wall_clock_of_the_answer_offset()
    {
        var learner = new Learner();
        learner.Answer("a", Japanese, true, Jst.At(Day, 23, 59, 59));
        learner.Answer("b", Japanese, true, Jst.At(Day.AddDays(1), 0, 0, 0));

        Assert.Equal(1, On(learner.Progress, Day));
        Assert.Equal(1, On(learner.Progress, Day.AddDays(1)));
    }

    [Fact]
    public void Day_of_uses_the_offset_not_utc()
    {
        // 日本時間 0:30 は UTC では前日だが、端末のローカル(+9:00)の日付で数える
        var at = new DateTimeOffset(2026, 11, 2, 0, 30, 0, TimeSpan.FromHours(9));

        Assert.Equal(new DateOnly(2026, 11, 2), DailyActivity.DayOf(at));
        Assert.Equal(new DateOnly(2026, 11, 1), DailyActivity.DayOf(at.ToUniversalTime()));
    }

    [Fact]
    public void Recording_since_is_set_by_the_first_count_and_never_moves()
    {
        var learner = new Learner();
        learner.Answer("a", Japanese, true, Jst.At(Day, 10));
        learner.Answer("b", Japanese, true, Jst.At(Day.AddDays(3), 10));

        Assert.Equal(Day, learner.Progress.Daily.RecordingSince);
        Assert.Null(On(learner.Progress, Day.AddDays(-1)));
        Assert.Equal(0, On(learner.Progress, Day.AddDays(1)));
    }

    [Fact]
    public void Answer_dated_before_recording_since_moves_recording_since_back_and_stays_valid()
    {
        // 時計を戻した・西のタイムゾーンへ移った: 記録開始日より前の日の採点も数え、検査で壊れた扱いにしない
        var learner = new Learner();
        learner.Answer("a", Japanese, true, Jst.At(Day, 10));
        learner.Answer("b", Japanese, true, Jst.At(Day.AddDays(-2), 10));

        Assert.Equal(Day.AddDays(-2), learner.Progress.Daily.RecordingSince);
        Assert.Equal(1, On(learner.Progress, Day.AddDays(-2)));
        Assert.Equal(1, On(learner.Progress, Day));
        Assert.Empty(ProgressValidator.Validate(learner.Progress, learner.Settings, Jst.At(Day, 12)));
    }

    [Fact]
    public void Count_stops_at_the_upper_limit_and_the_validator_rejects_values_above_it()
    {
        var atLimit = new DailyActivity { RecordingSince = Day, Days = [new DailyCount(Day, DailyActivity.MaxGradedPerDay)] };
        var below = new DailyActivity { RecordingSince = Day, Days = [new DailyCount(Day, DailyActivity.MaxGradedPerDay - 1)] };
        var above = new DailyActivity { RecordingSince = Day, Days = [new DailyCount(Day, DailyActivity.MaxGradedPerDay + 1)] };
        var huge = new DailyActivity { RecordingSince = Day, Days = [new DailyCount(Day, int.MaxValue)] };
        IReadOnlyList<ProgressIssue> Issues(DailyActivity d) =>
            ProgressValidator.Validate(LearningProgress.Empty with { Daily = d }, DefaultSettings.Create(), Jst.At(Day));

        Assert.Equal(DailyActivity.MaxGradedPerDay, below.Count(Day).CountOn(Day));
        Assert.Equal(DailyActivity.MaxGradedPerDay, atLimit.Count(Day).CountOn(Day));
        Assert.Equal(DailyActivity.MaxGradedPerDay, huge.Count(Day).CountOn(Day)); // 負に回らない
        Assert.Empty(Issues(atLimit.Count(Day)));
        Assert.Contains(Issues(above), i => i.Code == "daily.count");
        Assert.Contains(Issues(huge), i => i.Code == "daily.count");
    }

    [Fact]
    public void Retry_as_the_first_ever_counted_answer_is_impossible_so_retry_never_starts_recording()
    {
        // 記録の無い単語の再練習は例外(既存規則)。記録のある単語の再練習だけでは記録開始日は入らない
        var progress = LearningProgress.Empty with
        {
            Words = ImmutableDictionary<string, WordProgress>.Empty.WithComparers(StringComparer.Ordinal).Add("a", new WordProgress { WordId = "a" }),
        };
        var learner = new Learner { Progress = progress };
        learner.Answer("a", Japanese, true, Jst.At(Day, 10), AttemptType.Retry);

        Assert.Null(learner.Progress.Daily.RecordingSince);
        Assert.Empty(learner.Progress.Daily.Days);
    }

    [Fact]
    public void Abandon_keeps_the_daily_counts()
    {
        var r = new Runner(S.Start(S.Settings(1), S.Q("a"), S.Q("b")));
        r.Answer(true, Jst.At(Day, 10));

        var abandoned = SessionManager.Abandon(r.Session, r.Progress);

        Assert.Equal(1, On(abandoned, Day));
    }

    [Fact]
    public void Session_answer_skip_reveal_and_retry_follow_the_counting_rule()
    {
        // ①: A 正解・B スキップ・C 誤答 → 2問 / ②: 後回しの B で答えを見る → 3問。B・C の再練習でも 3問
        var r = new Runner(S.Start(S.Settings(1), S.Q("a"), S.Q("b"), S.Q("c")));
        r.Answer(true, Jst.At(Day, 10));
        r.Skip(Jst.At(Day, 10, 1));
        r.Answer(false, Jst.At(Day, 10, 2));
        Assert.Equal(2, On(r.Progress, Day));

        r.Apply(SessionManager.Reveal(r.Session, r.Progress, Jst.At(Day, 10, 3)));
        Assert.Equal(3, On(r.Progress, Day));

        while (SessionManager.Current(r.Session) is not null)
        {
            Assert.Equal(QuestionPhase.Retry, SessionManager.Phase(r.Session));
            r.Answer(true, Jst.At(Day, 10, 4));
        }

        Assert.Equal(3, On(r.Progress, Day));
    }

    [Fact]
    public void Skip_before_midnight_and_reveal_after_counts_on_the_next_day()
    {
        var r = new Runner(S.Start(S.Settings(1), S.Q("a")));
        r.Skip(Jst.At(Day, 23, 59));
        r.Apply(SessionManager.Reveal(r.Session, r.Progress, Jst.At(Day.AddDays(1), 0, 1)));

        Assert.Null(On(r.Progress, Day));
        Assert.Equal(1, On(r.Progress, Day.AddDays(1)));
    }
}

public class DailyProgressTests
{
    private static readonly DateOnly Today = Jst.Day0;

    private static LearningProgress WithWord() => LearningProgress.Empty with
    {
        Words = ImmutableDictionary<string, WordProgress>.Empty.WithComparers(StringComparer.Ordinal)
            .Add("a", new WordProgress { WordId = "a", State = LearningState.Weak }),
    };

    [Fact]
    public void Empty_progress_is_a_known_zero()
    {
        var today = DailyProgress.From(LearningProgress.Empty, Today, 10);

        Assert.Equal(0, today.Graded);
        Assert.False(today.Achieved);
        Assert.False(today.NoRecord);
    }

    [Fact]
    public void Legacy_progress_without_daily_records_has_no_record_instead_of_zero()
    {
        var today = DailyProgress.From(WithWord(), Today, 10);

        Assert.Null(today.Graded);
        Assert.True(today.NoRecord);
        Assert.False(today.Achieved);
    }

    [Fact]
    public void Day_after_recording_started_without_answers_is_zero()
    {
        var progress = WithWord() with { Daily = DailyActivity.Empty.Count(Today.AddDays(-1)) };

        Assert.Equal(0, DailyProgress.From(progress, Today, 10).Graded);
    }

    [Fact]
    public void Clock_moved_back_before_recording_started_has_no_record()
    {
        var progress = WithWord() with { Daily = DailyActivity.Empty.Count(Today) };

        Assert.Null(DailyProgress.From(progress, Today.AddDays(-1), 10).Graded);
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(12, true)]
    public void Achieved_at_or_above_the_goal_and_the_real_count_is_kept(int graded, bool achieved)
    {
        var daily = DailyActivity.Empty;
        for (var i = 0; i < graded; i++)
        {
            daily = daily.Count(Today);
        }

        var today = DailyProgress.From(WithWord() with { Daily = daily }, Today, 10);

        Assert.Equal(graded, today.Graded);
        Assert.Equal(achieved, today.Achieved);
        Assert.Equal(Math.Max(10 - graded, 0), today.Remaining);
    }

    [Fact]
    public void Changing_the_goal_does_not_change_the_count()
    {
        var progress = WithWord() with { Daily = DailyActivity.Empty.Count(Today).Count(Today).Count(Today) };

        var ten = DailyProgress.From(progress, Today, 10);
        var five = DailyProgress.From(progress, Today, 5);

        Assert.Equal((3, 10), (ten.Graded!.Value, ten.Goal));
        Assert.Equal((3, 5), (five.Graded!.Value, five.Goal));
    }
}

public class DailyActivityPersistenceTests
{
    private static string Json(string daily) => $$"""
        { "schemaVersion": 2, "wordSetVersion": "v", "sourceVersion": "s",
          "progress": { "words": {}, "appliedAnswerIds": []{{daily}} } }
        """;

    [Fact]
    public void Daily_round_trips_through_json()
    {
        var daily = DailyActivity.Empty.Count(Jst.Day0).Count(Jst.Day0).Count(Jst.Day0.AddDays(1));
        var document = new ProgressDocument { Progress = LearningProgress.Empty with { Daily = daily } };

        var json = ProgressJson.Serialize(document);
        var read = ProgressJson.Parse(json);

        Assert.Contains("\"recordingSince\": \"2026-11-01\"", json);
        Assert.Equal(Jst.Day0, read.Progress.Daily.RecordingSince);
        Assert.Equal([(Jst.Day0, 2), (Jst.Day0.AddDays(1), 1)], read.Progress.Daily.Days.Select(d => (d.Date, d.Graded)).ToArray());
    }

    [Fact]
    public void Missing_daily_reads_as_no_record()
    {
        var read = ProgressJson.Parse(Json(""));

        Assert.Null(read.Progress.Daily.RecordingSince);
        Assert.Empty(read.Progress.Daily.Days);
    }

    [Fact]
    public void Empty_daily_does_not_write_a_null_recording_since()
    {
        var json = ProgressJson.Serialize(new ProgressDocument());

        Assert.DoesNotContain("recordingSince", json);
        Assert.Contains("\"daily\"", json);
    }

    [Theory]
    [InlineData(""", "daily": null""")]
    [InlineData(""", "daily": { "days": null }""")]
    [InlineData(""", "daily": { "recordingSince": "2026-11-01", "days": [ null ] }""")]
    [InlineData(""", "daily": { "recordingSince": "2026-11-01", "days": [ { "date": null, "graded": 1 } ] }""")]
    [InlineData(""", "daily": { "recordingSince": "x", "days": [] }""")]
    public void Null_or_malformed_daily_is_unreadable(string daily)
    {
        Assert.Throws<InvalidDataException>(() => ProgressJson.Parse(Json(daily)));
    }

    [Theory]
    [InlineData("""{ "recordingSince": "2026-11-01", "days": [ { "date": "2026-11-01", "graded": -1 } ] }""", "daily.count")]
    [InlineData("""{ "recordingSince": "2026-11-01", "days": [ { "date": "2026-11-01", "graded": 1 }, { "date": "2026-11-01", "graded": 2 } ] }""", "daily.duplicate")]
    [InlineData("""{ "days": [ { "date": "2026-11-01", "graded": 1 } ] }""", "daily.since")]
    [InlineData("""{ "recordingSince": "2026-11-02", "days": [ { "date": "2026-11-01", "graded": 1 } ] }""", "daily.since")]
    public void Validator_reports_impossible_daily_records_as_critical(string daily, string code)
    {
        var progress = ProgressJson.Parse(Json($", \"daily\": {daily}")).Progress;

        var issues = ProgressValidator.Validate(progress, DefaultSettings.Create(), Jst.At(Jst.Day0.AddDays(5)));

        Assert.Contains(issues, i => i.Code == code && i.Severity == ProgressIssueSeverity.Critical);
    }

    [Fact]
    public void Validator_accepts_a_normal_daily_record()
    {
        var progress = LearningProgress.Empty with { Daily = DailyActivity.Empty.Count(Jst.Day0).Count(Jst.Day0.AddDays(2)) };

        Assert.Empty(ProgressValidator.Validate(progress, DefaultSettings.Create(), Jst.At(Jst.Day0.AddDays(5))));
    }
}

public class GoalSettingsTests
{
    [Fact]
    public void Default_daily_goal_is_ten_and_separate_from_question_count()
    {
        var settings = DefaultSettings.Create();

        Assert.Equal(10, settings.Goal.DailyQuestionGoal);
        Assert.Equal([5, 10, 20, 30, 50], settings.Goal.DailyQuestionGoalChoices.ToArray());
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(SettingsLimits.MaxDailyGoal, false)]
    [InlineData(SettingsLimits.MaxDailyGoal + 1, true)]
    public void Daily_goal_outside_the_range_is_reported(int goal, bool reported)
    {
        var settings = DefaultSettings.Create() with { Goal = new GoalSettings { DailyQuestionGoal = goal } };

        Assert.Equal(reported, SettingsValidator.Validate(settings).Any(i => i.Path == "goal.dailyQuestionGoal"));
    }

    [Fact]
    public void Empty_or_out_of_range_goal_choices_are_reported()
    {
        var empty = DefaultSettings.Create() with { Goal = new GoalSettings { DailyQuestionGoalChoices = [] } };
        var outside = DefaultSettings.Create() with { Goal = new GoalSettings { DailyQuestionGoalChoices = [0, 10] } };

        Assert.Contains(SettingsValidator.Validate(empty), i => i.Path == "goal.dailyQuestionGoalChoices");
        Assert.Contains(SettingsValidator.Validate(outside), i => i.Path == "goal.dailyQuestionGoalChoices");
    }

    [Fact]
    public void Missing_goal_in_settings_json_uses_the_default_and_null_is_unreadable()
    {
        Assert.Equal(10, LearningSettingsJson.Parse("""{ "settingsVersion": 1 }""").Goal.DailyQuestionGoal);
        Assert.Equal(30, LearningSettingsJson.Parse("""{ "goal": { "dailyQuestionGoal": 30 } }""").Goal.DailyQuestionGoal);
        Assert.Throws<InvalidDataException>(() => LearningSettingsJson.Parse("""{ "goal": null }"""));
        Assert.Throws<InvalidDataException>(() => LearningSettingsJson.Parse("""{ "goal": { "dailyQuestionGoalChoices": null } }"""));
    }
}
