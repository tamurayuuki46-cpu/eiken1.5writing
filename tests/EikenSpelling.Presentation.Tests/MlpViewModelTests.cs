using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;
using EikenSpelling.Flow.Tests;
using EikenSpelling.Persistence;
using EikenSpelling.Presentation.Screens;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Presentation.Tests;

/// <summary>
/// ロードマップ④: ホームの今日の目標と実績・再開の選択の文脈・設定画面(S-07)。本物の Flow(一時フォルダ)で確かめる。
/// </summary>
public class MlpViewModelTests
{
    private static void AnswerAndNext(ShellViewModel shell, ManualTimeProvider time, bool correct)
    {
        shell.AnswerCurrent(time, correct);
        shell.Run(time, s => ((FeedbackViewModel)s).NextCommand);
    }

    private static SettingsViewModel OpenSettings(ShellEnv env, ShellViewModel shell)
    {
        shell.Run(env.Time, s => ((HomeViewModel)s).OpenSettingsCommand);
        return shell.As<SettingsViewModel>();
    }

    [Fact]
    public void First_run_home_shows_zero_of_the_default_goal()
    {
        using var env = new ShellEnv();
        var home = env.Start().As<HomeViewModel>();

        Assert.Equal("今日 0 / 10問", home.TodayText);
        Assert.Equal("目標まであと10問", home.TodayNote);
    }

    [Fact]
    public void Home_after_a_finished_practice_shows_the_real_count_and_achievement_above_the_goal()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        var settings = OpenSettings(env, shell);
        settings.SelectedDailyGoal = 5;
        shell.Run(env.Time, s => ((SettingsViewModel)s).SaveCommand);

        shell.StartPractice(env.Time, count: 5);
        while (shell.Current is QuestionViewModel)
        {
            AnswerAndNext(shell, env.Time, true);
        }

        shell.Run(env.Time, s => ((ResultViewModel)s).GoHomeCommand);
        var home = shell.As<HomeViewModel>();
        Assert.Equal("今日 5 / 5問", home.TodayText);
        Assert.Equal("目標達成", home.TodayNote);
        Assert.True(home.TodayAchieved);
    }

    [Fact]
    public void Legacy_record_shows_no_record_instead_of_zero()
    {
        using var env = new ShellEnv();
        env.Dir.Write(ProgressRepository.FileName, """
            { "schemaVersion": 2, "wordSetVersion": "v1", "sourceVersion": "src1",
              "progress": { "words": { "apple": { "wordId": "apple", "state": "Normal", "lastNormalAskedAt": "2026-10-01T10:00:00+09:00",
                "modes": { "Japanese": { "normalCorrect": 1 } } } }, "appliedAnswerIds": [] } }
            """);

        var home = env.Start().As<HomeViewModel>();

        Assert.Equal("今日 記録なし", home.TodayText);
        Assert.Contains("次に採点したときから記録します", home.TodayNote);
    }

    [Fact]
    public void Resume_prompt_says_which_practice_the_remaining_count_and_today_without_counting()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        shell.StartPractice(env.Time, count: 5);
        AnswerAndNext(shell, env.Time, true);
        AnswerAndNext(shell, env.Time, false);
        env.Prompt.Answer = true;
        shell.Run(env.Time, s => ((QuestionViewModel)s).QuitCommand);

        var prompt = shell.As<ResumePromptViewModel>();
        Assert.Equal("自動ミックス・通常5問の練習", prompt.KindText);
        Assert.Equal("残り 3問(誤答すると再練習が加わります)", prompt.RemainingText);
        Assert.Equal("今日 2 / 10問", prompt.TodayText);

        var before = env.ProgressJsonText;
        shell.Run(env.Time, s => ((ResumePromptViewModel)s).ResumeCommand);
        shell.As<QuestionViewModel>();
        Assert.Equal(before, env.ProgressJsonText);
    }

    [Fact]
    public void Settings_screen_edits_the_basic_settings_and_the_daily_goal_and_saves_them()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        var settings = OpenSettings(env, shell);
        Assert.True(settings.CanSave);
        Assert.Equal(10, settings.SelectedDailyGoal);
        Assert.Equal([5, 10, 20, 30, 50], settings.DailyGoalOptions.ToArray());
        Assert.Equal(10, settings.SelectedQuestionCount);

        settings.SelectedDailyGoal = 20;
        settings.SelectedQuestionCount = 5;
        settings.SelectedMode = settings.ModeOptions.First(o => !o.AutoMix && o.Mode == Blank);
        shell.Run(env.Time, s => ((SettingsViewModel)s).SaveCommand);

        var home = shell.As<HomeViewModel>();
        Assert.Equal("今日 0 / 20問", home.TodayText);
        Assert.Equal(5, home.SelectedQuestionCount);
        Assert.Equal("設定を保存しました。", home.SavedMessage);
        Assert.Null(home.WarningMessage);
        var file = env.Dir.Read(SettingsRepository.FileName)!;
        Assert.Contains("\"dailyQuestionGoal\": 20", file);
        Assert.Contains("\"manualMode\": \"Blank\"", file);
    }

    [Fact]
    public void Settings_reset_returns_the_screen_values_and_close_writes_nothing()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        var settings = OpenSettings(env, shell);
        settings.SelectedDailyGoal = 50;
        settings.SelectedQuestionCount = 20;

        shell.Run(env.Time, s => ((SettingsViewModel)s).ResetCommand);
        var reset = shell.As<SettingsViewModel>();
        Assert.Equal((10, 10), (reset.SelectedDailyGoal, reset.SelectedQuestionCount));

        shell.Run(env.Time, s => ((SettingsViewModel)s).CloseCommand);
        shell.As<HomeViewModel>();
        Assert.Null(env.Dir.Read(SettingsRepository.FileName));
    }

    [Fact]
    public void Settings_save_failure_keeps_the_typed_values_and_shows_the_message()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        var settings = OpenSettings(env, shell);
        settings.SelectedDailyGoal = 30;
        env.Files.FailWrites = true;

        shell.Run(env.Time, s => ((SettingsViewModel)s).SaveCommand);

        var failed = shell.As<SettingsViewModel>();
        Assert.Equal(30, failed.SelectedDailyGoal);
        Assert.NotNull(failed.Message);
    }

    [Fact]
    public void Unusable_manual_mode_cannot_be_saved_from_the_settings_screen_options()
    {
        using var env = new ShellEnv { Modes = [Japanese, Blank] };
        var settings = OpenSettings(env, env.Start());

        var audio = settings.ModeOptions.Single(o => !o.AutoMix && o.Mode == Audio);
        Assert.False(audio.Available);
        settings.SelectedMode = audio;
        Assert.False(settings.SaveCommand.CanExecute(null));
        Assert.NotNull(settings.SelectedModeUnavailableNote);
    }
}
