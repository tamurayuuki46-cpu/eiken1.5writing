using System.Collections.Immutable;

using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;
using EikenSpelling.Persistence;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Flow.Tests;

/// <summary>
/// ロードマップ④ の受入シナリオ①〜⑦(.steering/20261115-mlp-daily-goal/requirements.md 4章)。
/// 日別計上ルール(2026-11-15 本人確定): 通常枠の採点を1回、日付は採点確定時の端末のローカル日付。
/// 時計の端末のタイムゾーンは +9:00(FixedTimeProvider.Zone)。
/// </summary>
public class FlowDailyGoalTests
{
    private const string A = "alpha";
    private const string B = "bravo";
    private const string C = "charlie";

    private static readonly Guid SessionId = new("00000000-0000-0000-0000-00000000d00d");
    private static readonly TimeSpan Jst = TimeSpan.FromHours(9);
    private static readonly DateOnly Day1 = new(2026, 11, 1);
    private static readonly DateOnly Day2 = Day1.AddDays(1);

    private static DateTimeOffset At(DateOnly day, int hour, int minute) => new(day.ToDateTime(new TimeOnly(hour, minute)), Jst);

    /// <summary>A・B・C の順の未完了セッション(日本語、まだ答えていない)を保存済みの環境。</summary>
    private static FlowEnv EnvWithFixedOrder(LearningProgress? progress = null)
    {
        var env = new FlowEnv { WordsJson = Words.Json(new TestWord(A), new TestWord(B), new TestWord(C)) };
        LearningMode[] eligible = [Japanese, Audio, Flash];
        var session = SessionManager.Start(
            SessionId,
            FlowEnv.T0,
            [new(A, A, Japanese, [.. eligible]), new(B, B, Japanese, [.. eligible]), new(C, C, Japanese, [.. eligible])],
            DefaultSettings.Create());
        env.Dir.Write(
            ProgressRepository.FileName,
            ProgressJson.Serialize(new ProgressDocument
            {
                WordSetVersion = "v1", SourceVersion = "src1", Progress = progress ?? LearningProgress.Empty, Session = session,
            }));
        return env;
    }

    private static PracticeFlow Resume(FlowEnv env)
    {
        var flow = env.Start();
        flow.Resume();
        return flow;
    }

    private static DailyActivity Saved(FlowEnv env) => env.Progress().Progress.Daily;

    private static void Answer(PracticeFlow flow, bool correct)
    {
        flow.AnswerCurrent(correct);
        flow.Next();
    }

    /// <summary>残りをすべて正答して結果からホームへ。</summary>
    private static HomeScreen FinishAndGoHome(PracticeFlow flow)
    {
        flow.AnswerAll(_ => true);
        flow.GoHome();
        return flow.As<HomeScreen>();
    }

    [Fact]
    public void Scenario1_answer_A_skip_B_wrong_C_counts_two_today()
    {
        using var env = EnvWithFixedOrder();
        var flow = Resume(env);

        Answer(flow, true);
        flow.Skip();
        Answer(flow, false);

        Assert.Equal(2, Saved(env).CountOn(Day1));
        Assert.Equal((B, QuestionPhase.Deferred), (flow.As<QuestionScreen>().Question.WordId, flow.As<QuestionScreen>().Phase));
    }

    [Fact]
    public void Scenario2_reveal_on_deferred_B_counts_three_and_retries_keep_three()
    {
        using var env = EnvWithFixedOrder();
        var flow = Resume(env);
        Answer(flow, true);
        flow.Skip();
        Answer(flow, false);

        flow.Reveal();
        flow.Next();
        Assert.Equal(3, Saved(env).CountOn(Day1));

        // B・C の再練習
        var retries = 0;
        while (flow.Screen is QuestionScreen q)
        {
            Assert.True(q.Question.IsRetry);
            retries++;
            Answer(flow, true);
        }

        Assert.Equal(2, retries);
        flow.GoHome();
        var today = flow.As<HomeScreen>().Today;
        Assert.Equal((3, 10), (today.Graded!.Value, today.Goal));
        Assert.Equal(3, Saved(env).CountOn(Day1));
    }

    [Fact]
    public void Scenario3_save_failure_retry_and_restart_neither_lose_nor_double_count()
    {
        using var env = EnvWithFixedOrder();
        var flow = Resume(env);
        Answer(flow, true);

        env.Files.FailWrites = true;
        flow.AnswerCurrent(false);
        flow.RetrySave();
        Assert.False(flow.As<FeedbackScreen>().Saved);
        env.Files.FailWrites = false;
        flow.RetrySave();
        Assert.True(flow.As<FeedbackScreen>().Saved);
        Assert.Equal(2, Saved(env).CountOn(Day1));

        // 再起動 → 再開: 成績も日別実績も2問のまま、続きは C
        var restarted = env.Start();
        Assert.Equal(2, restarted.As<ResumePromptScreen>().Today.Graded);
        restarted.Resume();
        Assert.Equal(C, restarted.As<QuestionScreen>().Question.WordId);
        Assert.Equal(2, env.RecordedAnswers());
        Assert.Equal(2, Saved(env).CountOn(Day1));

        Assert.Equal(3, FinishAndGoHome(restarted).Today.Graded);
        Assert.Equal(3, Saved(env).CountOn(Day1));
        Assert.Equal(4, env.RecordedAnswers()); // 通常3 + B の再練習1(再練習は記録するが日別には数えない)
    }

    [Fact]
    public void Scenario3_restart_after_an_unsaved_answer_counts_only_when_answered_again()
    {
        using var env = EnvWithFixedOrder();
        var flow = Resume(env);
        env.Files.FailWrites = true;
        flow.AnswerCurrent(true); // 保存できないまま終了

        env.Files.FailWrites = false;
        var restarted = env.Start();
        Assert.Equal(0, restarted.As<ResumePromptScreen>().Today.Graded);
        restarted.Resume();
        Assert.Equal(A, restarted.As<QuestionScreen>().Question.WordId);
        Answer(restarted, true);

        Assert.Equal(1, Saved(env).CountOn(Day1));
    }

    [Fact]
    public void Scenario4_skip_at_2359_and_reveal_at_0001_counts_on_the_next_day()
    {
        using var env = EnvWithFixedOrder();
        env.Time.Now = At(Day1, 23, 59);
        var flow = Resume(env);
        flow.Skip();
        Answer(flow, true);
        Answer(flow, true);
        Assert.Equal(2, Saved(env).CountOn(Day1));

        env.Time.Now = At(Day2, 0, 1);
        flow.Reveal();

        Assert.Equal(2, Saved(env).CountOn(Day1));
        Assert.Equal(1, Saved(env).CountOn(Day2));
    }

    [Fact]
    public void Scenario4_answer_at_2359_and_resave_next_day_counts_on_the_previous_day()
    {
        using var env = EnvWithFixedOrder();
        env.Time.Now = At(Day1, 23, 59);
        var flow = Resume(env);

        env.Files.FailWrites = true;
        flow.AnswerCurrent(true);
        env.Time.Now = At(Day2, 0, 1);
        env.Files.FailWrites = false;
        flow.RetrySave();

        Assert.True(flow.As<FeedbackScreen>().Saved);
        Assert.Equal(1, Saved(env).CountOn(Day1));
        Assert.Equal(0, Saved(env).CountOn(Day2));

        // 翌日に残りの B・C を答える → 今日は2問だけ(前日の1問は今日に移らない)
        flow.Next();
        Assert.Equal(2, FinishAndGoHome(flow).Today.Graded);
        Assert.Equal(1, Saved(env).CountOn(Day1));
    }

    [Fact]
    public void Scenario5_next_day_resume_of_a_deferred_set_session_shows_phase_and_remaining_and_counts_nothing()
    {
        using var h = new SetHarness(total: 60);
        var flow = h.Start();
        flow.OpenSets();
        flow.PreviewSetPractice(Choices.Manual(Japanese, 3));
        var first = flow.As<QuestionScreen>().Question.WordId;
        flow.Skip();
        Answer(flow, true);
        Answer(flow, false);
        Assert.Equal((first, QuestionPhase.Deferred), (flow.As<QuestionScreen>().Question.WordId, flow.As<QuestionScreen>().Phase));
        flow.Quit();
        Assert.Equal(2, h.Env.Progress().Progress.Daily.CountOn(Day1));

        // 翌日に起動
        h.Env.Time.Now = At(Day2, 8, 0);
        var before = h.ProgressText;
        var next = h.Start();
        var prompt = next.As<ResumePromptScreen>();
        Assert.True(prompt.CanResume);
        Assert.Equal(QuestionPhase.Deferred, prompt.Session.Phase);
        Assert.Equal(1, prompt.Session.Skipped);
        Assert.Equal(1, prompt.Session.Remaining);
        Assert.True(prompt.Session.MayAddRetries);
        Assert.Equal((false, Japanese, false, 3), (prompt.Session.AutoMix, prompt.Session.ManualMode, prompt.Session.WeakOnly, prompt.Session.NormalCount));
        Assert.Equal((50, 1, 2), (prompt.Session.Set!.UnitSize, prompt.Session.Set.Number, prompt.Session.Set.Total));
        Assert.Equal(0, prompt.Today.Graded);

        next.Resume();
        Assert.Equal((first, QuestionPhase.Deferred), (next.As<QuestionScreen>().Question.WordId, next.As<QuestionScreen>().Phase));
        Assert.Equal(before, h.ProgressText); // 再開だけでは書かない(日別も増えない)
        Assert.Equal(0, h.Env.Progress().Progress.Daily.CountOn(Day2));

        // 後回しの問題に答えると今日の1問
        Answer(next, true);
        Assert.Equal(1, h.Env.Progress().Progress.Daily.CountOn(Day2));
    }

    [Fact]
    public void Scenario6_lowering_the_goal_keeps_the_count_and_the_question_count_and_progress()
    {
        using var env = EnvWithFixedOrder();
        var flow = Resume(env);
        var home = FinishAndGoHome(flow);
        Assert.Equal((3, 10), (home.Today.Graded!.Value, home.Today.Goal));
        var progressBefore = env.ProgressJsonText;

        flow.OpenSettings();
        var settings = flow.As<SettingsScreen>();
        Assert.Equal(10, settings.DailyGoal);
        Assert.Equal([5, 10, 20, 30, 50], settings.DailyGoalChoices.ToArray());
        flow.SaveSettings(settings.Values, dailyGoal: 5);

        var after = flow.As<HomeScreen>();
        Assert.Equal((3, 5), (after.Today.Graded!.Value, after.Today.Goal));
        Assert.Equal((false, 2), (after.Today.Achieved, after.Today.Remaining));
        Assert.Equal(settings.Values.QuestionCount, after.Defaults.QuestionCount);
        Assert.Equal(progressBefore, env.ProgressJsonText); // 成績・日別実績は書き換えない

        // 再起動しても目標5・実績3
        var restarted = env.Start().As<HomeScreen>().Today;
        Assert.Equal((3, 5), (restarted.Graded!.Value, restarted.Goal));
    }

    [Fact]
    public void Scenario6_goal_change_does_not_touch_the_saved_plan_of_an_unfinished_session()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.OpenSettings();
        flow.SaveSettings(flow.As<SettingsScreen>().Values, dailyGoal: 20);
        flow.StartRecommended();
        var saved = env.Progress().Session!;

        Assert.Equal(20, saved.Settings.Goal.DailyQuestionGoal);
        Assert.Equal(10, saved.Settings.Practice.QuestionCount);
        Assert.Equal(5, saved.NormalCount); // 単語は5語だけ。目標20は出題数に効かない
    }

    [Fact]
    public void Scenario7_legacy_record_keeps_states_and_shows_no_record_until_the_first_new_answer()
    {
        using var env = new FlowEnv();
        var legacy = $$"""
            { "schemaVersion": 1, "wordSetVersion": "v1", "sourceVersion": "src1",
              "progress": { "words": {
                "apple": { "wordId": "apple", "state": "Weak", "lastNormalAskedAt": "2026-10-01T10:00:00+09:00",
                           "modes": { "Japanese": { "normalWrong": 4, "lastWrongAt": "2026-10-01T10:00:00+09:00" } } },
                "banana": { "wordId": "banana", "state": "Normal", "lastNormalAskedAt": "2026-10-02T10:00:00+09:00",
                            "modes": { "Japanese": { "normalCorrect": 9 } } }
              }, "appliedAnswerIds": [] } }
            """;
        env.Dir.Write(ProgressRepository.FileName, legacy);
        var before = env.Dir.Snapshot();

        var flow = env.Start();
        var home = flow.As<HomeScreen>();
        Assert.True(home.Today.NoRecord);
        Assert.Equal((1, 1), (home.Status.Weak, home.Status.Normal));
        Assert.Equal(before, env.Dir.Snapshot()); // 起動・ホーム閲覧では書かない

        flow.StartPractice(Choices.Auto(1));
        Answer(flow, true);
        flow.GoHome();
        Assert.Equal(1, flow.As<HomeScreen>().Today.Graded);
        Assert.Equal(Day1, Saved(env).RecordingSince);
        Assert.Null(Saved(env).CountOn(Day1.AddDays(-1))); // 記録開始前は記録なしのまま
        Assert.Equal(LearningState.Weak, env.Progress().Progress.Words["apple"].State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(SettingsLimits.MaxDailyGoal + 1)]
    public void Out_of_range_goal_is_not_saved_and_the_screen_keeps_the_value(int goal)
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.OpenSettings();
        flow.SaveSettings(flow.As<SettingsScreen>().Values, goal);

        var screen = flow.As<SettingsScreen>();
        Assert.Equal(goal, screen.DailyGoal);
        Assert.Contains(screen.Issues, i => i.Path == "goal.dailyQuestionGoal");
        Assert.Null(env.Dir.Read(SettingsRepository.FileName));
        flow.CloseSettings();
        Assert.Equal(10, flow.As<HomeScreen>().Today.Goal);
    }

    [Fact]
    public void Goal_outside_the_choices_but_within_the_range_is_saved_exactly()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.OpenSettings();
        flow.SaveSettings(flow.As<SettingsScreen>().Values, 37);

        Assert.Equal(37, flow.As<HomeScreen>().Today.Goal);
        Assert.Equal(37, env.Start().As<HomeScreen>().Today.Goal);
    }

    [Fact]
    public void Reset_returns_the_goal_to_the_default_without_saving_and_close_discards_it()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.OpenSettings();
        flow.SaveSettings(flow.As<SettingsScreen>().Values, 30);
        flow.OpenSettings();
        Assert.Equal(30, flow.As<SettingsScreen>().DailyGoal);
        var saved = env.Dir.Read(SettingsRepository.FileName);

        flow.ResetSettings();
        Assert.Equal(10, flow.As<SettingsScreen>().DailyGoal);
        Assert.Equal(saved, env.Dir.Read(SettingsRepository.FileName));
        flow.CloseSettings();

        Assert.Equal(30, flow.As<HomeScreen>().Today.Goal);
    }

    [Fact]
    public void Goal_save_failure_stays_on_settings_with_the_typed_goal_and_retry_succeeds()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.OpenSettings();
        env.Files.FailWrites = true;
        flow.SaveSettings(flow.As<SettingsScreen>().Values, 20);

        var failed = flow.As<SettingsScreen>();
        Assert.Equal(20, failed.DailyGoal);
        Assert.NotNull(failed.Message);

        env.Files.FailWrites = false;
        flow.SaveSettings(failed.Values); // 画面の値(20)で保存し直す
        Assert.Equal(20, flow.As<HomeScreen>().Today.Goal);
    }

    [Fact]
    public void Home_recomputes_today_after_midnight_when_returning_to_home()
    {
        using var env = EnvWithFixedOrder();
        var flow = Resume(env);
        Assert.Equal(3, FinishAndGoHome(flow).Today.Graded);

        env.Time.Now = At(Day2, 0, 5);
        flow.OpenWeakList();
        flow.CloseWeakList();

        var today = flow.As<HomeScreen>().Today;
        Assert.Equal((Day2, 0), (today.Today, today.Graded!.Value));
    }

    [Fact]
    public void Discarding_an_unfinished_session_keeps_the_daily_count()
    {
        using var env = EnvWithFixedOrder();
        var flow = Resume(env);
        Answer(flow, true);
        flow.Quit();

        flow.DiscardAndGoHome();

        Assert.Equal(1, flow.As<HomeScreen>().Today.Graded);
        Assert.Equal(1, Saved(env).CountOn(Day1));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void Resume_in_the_retry_phase_notes_added_retries_only_when_a_second_retry_is_possible(int retryCount, bool expected)
    {
        using var env = new FlowEnv { WordsJson = Words.Json(new TestWord(A), new TestWord(B)) };
        env.Dir.Write(SettingsRepository.FileName, $$"""{ "settingsVersion": 1, "practice": { "retryCount": {{retryCount}} } }""");
        var flow = env.Start();
        flow.StartPractice(Choices.Manual(Japanese, 2));
        Answer(flow, false);
        Answer(flow, false);
        Assert.True(flow.As<QuestionScreen>().Question.IsRetry);
        flow.Quit();

        var summary = flow.As<ResumePromptScreen>().Session;
        Assert.Equal((QuestionPhase.Retry, 2, expected), (summary.Phase!.Value, summary.Remaining, summary.MayAddRetries));
    }

    [Fact]
    public void Resume_without_retries_does_not_note_added_retries()
    {
        using var env = new FlowEnv { WordsJson = Words.Json(new TestWord(A), new TestWord(B)) };
        env.Dir.Write(SettingsRepository.FileName, """{ "settingsVersion": 1, "practice": { "retryCount": 0 } }""");
        var flow = env.Start();
        flow.StartPractice(Choices.Manual(Japanese, 2));
        Answer(flow, false);
        flow.Quit();

        var summary = flow.As<ResumePromptScreen>().Session;
        Assert.Equal((QuestionPhase.Normal, 1, false), (summary.Phase!.Value, summary.Remaining, summary.MayAddRetries));
    }

    [Fact]
    public void Clock_moved_back_before_recording_started_still_saves_and_reloads_as_loaded()
    {
        using var env = EnvWithFixedOrder();
        env.Time.Now = At(Day2, 10, 0);
        var flow = Resume(env);
        Answer(flow, true);

        env.Time.Now = At(Day1, 10, 0); // 時計を前日に戻す
        Answer(flow, true);
        Assert.True(flow.Screen is QuestionScreen);

        var restarted = env.Start();
        var prompt = restarted.As<ResumePromptScreen>();
        // 壊れた扱い(バックアップからの復旧)になっていない。時計のずれの警告は既存の規則どおり出る
        Assert.DoesNotContain(prompt.Notices, n => n.Contains("バックアップから復旧"));
        Assert.DoesNotContain(env.Dir.Snapshot().Keys, k => k.Contains("corrupt"));
        Assert.Equal(1, prompt.Today.Graded);
        Assert.Equal((Day1, 1, 1), (Saved(env).RecordingSince!.Value, Saved(env).CountOn(Day1)!.Value, Saved(env).CountOn(Day2)!.Value));
    }

    [Fact]
    public void Local_date_follows_the_device_time_zone_not_japan_time()
    {
        // 端末が UTC のとき、日本時間 8:00(= UTC 前日 23:00)の採点は端末の前日に数える
        using var env = EnvWithFixedOrder();
        env.Time.Zone = TimeZoneInfo.Utc;
        env.Time.Now = At(Day2, 8, 0);
        var flow = Resume(env);
        Answer(flow, true);

        Assert.Equal(1, Saved(env).CountOn(Day1));
        Assert.Equal(0, Saved(env).CountOn(Day2));
    }
}
