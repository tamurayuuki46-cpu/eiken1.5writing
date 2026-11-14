using EikenSpelling.Presentation.Logging;
using EikenSpelling.Presentation.Screens;

namespace EikenSpelling.Presentation.Tests;

/// <summary>ShellViewModel の骨: 画面の写し・受け付け開始のゲート・古い画面の操作・直前の回答の値・通知(design 4.3)。</summary>
public class ShellTests
{
    private static readonly TimeSpan JustUnder = TimeSpan.FromMilliseconds(290);

    [Fact]
    public void Startup_shows_home_and_writes_nothing()
    {
        using var env = new ShellEnv();

        var shell = env.Start();

        Assert.IsType<HomeViewModel>(shell.Current);
        Assert.Null(env.ProgressJsonText);
        Assert.Empty(env.Dir.Names());
    }

    // ---- 受け付け開始のゲート ----

    [Fact]
    public void Commands_before_begin_accepting_are_ignored_however_late()
    {
        using var env = new ShellEnv();
        var shell = new ShellViewModel(env.StartFlow(), new ShellOptions(env.Modes, env.Prompt, env.Log, env.Time));
        var home = shell.As<HomeViewModel>();

        env.Time.Advance(TimeSpan.FromMinutes(5));
        home.StartCommand.Execute(null);

        Assert.Same(home, shell.Current);
        Assert.Null(env.ProgressJsonText);
    }

    [Fact]
    public void Commands_within_the_gate_are_ignored_and_from_the_boundary_are_accepted()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        var home = shell.As<HomeViewModel>();

        env.Time.Advance(JustUnder);
        home.StartCommand.Execute(null);
        Assert.Same(home, shell.Current);
        Assert.Null(env.ProgressJsonText);

        env.Time.Advance(TimeSpan.FromMilliseconds(10)); // ちょうど 0.30 秒
        home.StartCommand.Execute(null);
        Assert.IsType<QuestionViewModel>(shell.Current);
        Assert.NotNull(env.ProgressJsonText);
    }

    [Fact]
    public void Every_command_of_every_practice_screen_goes_through_the_gate()
    {
        using var env = new ShellEnv();
        env.Prompt.Answer = true; // 確認が出たら「はい」(出ないことを確かめる)
        var shell = env.Start();

        AssertAllCommandsGated(env, shell); // ホーム
        shell.StartPractice(env.Time);
        AssertAllCommandsGated(env, shell); // 出題
        shell.AnswerCurrent(env.Time, correct: true);
        AssertAllCommandsGated(env, shell); // 判定
        while (shell.Current is not ResultViewModel)
        {
            if (shell.Current is FeedbackViewModel)
            {
                shell.Run(env.Time, s => ((FeedbackViewModel)s).NextCommand);
            }
            else
            {
                shell.AnswerCurrent(env.Time, correct: true);
            }
        }

        AssertAllCommandsGated(env, shell); // 結果
    }

    private static void AssertAllCommandsGated(ShellEnv env, ShellViewModel shell)
    {
        var screen = shell.Current;
        screen.BeginAccepting();
        var before = env.ProgressJsonText;
        Assert.NotEmpty(screen.Commands);

        foreach (var command in screen.Commands)
        {
            command.Execute(null);
        }

        Assert.Same(screen, shell.Current);
        Assert.Equal(before, env.ProgressJsonText);
        Assert.Empty(env.Prompt.Asked);
    }

    // ---- 古い画面の操作・連打 ----

    [Fact]
    public void Old_screen_commands_are_disabled_and_do_nothing_after_the_screen_changes()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        var home = shell.As<HomeViewModel>();
        var raised = 0;
        home.StartCommand.CanExecuteChanged += (_, _) => raised++;

        shell.StartPractice(env.Time); // 1回目は効く
        var question = shell.As<QuestionViewModel>();
        Assert.False(home.IsActive);
        Assert.False(home.StartCommand.CanExecute(null));
        Assert.True(raised > 0);

        env.Time.Advance(TimeSpan.FromSeconds(1));
        var before = env.ProgressJsonText;
        home.StartCommand.Execute(null); // 2回目(古い画面)

        Assert.Same(question, shell.Current);
        Assert.Equal(before, env.ProgressJsonText);
    }

    [Fact]
    public void Second_execution_of_answer_next_and_go_home_from_the_old_screen_does_nothing()
    {
        using var env = new ShellEnv();
        var shell = env.Start();

        void AssertOldCommandDoesNothing(ScreenViewModel old, Mvvm.RelayCommand command)
        {
            var current = shell.Current;
            Assert.NotSame(old, current); // 1回目で画面が変わっている
            env.Time.Advance(TimeSpan.FromSeconds(1)); // ゲートを過ぎてから古い画面のコマンドをもう一度
            var before = env.ProgressJsonText;
            command.Execute(null);
            Assert.Same(current, shell.Current);
            Assert.Equal(before, env.ProgressJsonText);
        }

        var question = shell.StartPractice(env.Time);
        question.Input = question.WordForTests;
        shell.Run(env.Time, s => ((QuestionViewModel)s).AnswerCommand);
        AssertOldCommandDoesNothing(question, question.AnswerCommand);

        while (shell.Current is not ResultViewModel)
        {
            if (shell.Current is FeedbackViewModel feedback)
            {
                shell.Run(env.Time, s => ((FeedbackViewModel)s).NextCommand);
                AssertOldCommandDoesNothing(feedback, feedback.NextCommand);
            }
            else
            {
                shell.AnswerCurrent(env.Time, correct: true);
            }
        }

        var result = shell.As<ResultViewModel>();
        shell.Run(env.Time, s => ((ResultViewModel)s).GoHomeCommand);
        AssertOldCommandDoesNothing(result, result.GoHomeCommand);
    }

    [Fact]
    public void Shell_ignores_operations_sent_from_a_screen_that_is_no_longer_current()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        var home = shell.As<HomeViewModel>();
        shell.StartPractice(env.Time);
        var question = shell.As<QuestionViewModel>();
        var before = env.ProgressJsonText;

        // コマンド側の確認を通らずに届いた場合でも、ShellViewModel が古い画面からの操作を止める
        shell.Invoke(home, flow => flow.StartPractice(home.Choice));

        Assert.Same(question, shell.Current);
        Assert.Equal(before, env.ProgressJsonText);
    }

    [Fact]
    public void Quick_second_click_lands_on_the_next_screen_and_is_ignored()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        shell.StartPractice(env.Time);
        var q = shell.As<QuestionViewModel>();
        q.Input = q.WordForTests;
        shell.Run(env.Time, s => ((QuestionViewModel)s).AnswerCommand);
        var feedback = shell.As<FeedbackViewModel>();

        env.Time.Advance(TimeSpan.FromMilliseconds(100)); // ダブルクリックの2回目が「次へ」に当たる
        feedback.NextCommand.Execute(null);

        Assert.Same(feedback, shell.Current);
    }

    [Fact]
    public void Repeated_go_home_then_start_within_the_gate_does_not_start_a_practice()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        shell.StartPractice(env.Time);
        while (shell.Current is not ResultViewModel)
        {
            shell.AnswerCurrent(env.Time, correct: true);
            shell.Run(env.Time, s => ((FeedbackViewModel)s).NextCommand);
        }

        shell.Run(env.Time, s => ((ResultViewModel)s).GoHomeCommand);
        var home = shell.As<HomeViewModel>();
        var before = env.ProgressJsonText;

        env.Time.Advance(TimeSpan.FromMilliseconds(100));
        home.StartCommand.Execute(null);

        Assert.Same(home, shell.Current);
        Assert.Equal(before, env.ProgressJsonText);
    }

    // ---- 直前の回答の値 ----

    [Fact]
    public void Last_answer_survives_retry_save_and_is_cleared_after_leaving_feedback()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        var q = shell.StartPractice(env.Time);
        var number = q.Number;
        var total = q.Total;
        env.Files.FailWrites = true;
        q.Input = "  " + q.WordForTests.ToUpperInvariant() + "X ";
        shell.Run(env.Time, s => ((QuestionViewModel)s).AnswerCommand);
        var failed = shell.As<FeedbackViewModel>();
        Assert.False(failed.Saved);

        env.Files.FailWrites = false;
        shell.Run(env.Time, s => ((FeedbackViewModel)s).RetrySaveCommand);

        var saved = shell.As<FeedbackViewModel>();
        Assert.NotSame(failed, saved); // 再保存で作り直される
        Assert.True(saved.Saved);
        Assert.Equal(number, saved.Number);
        Assert.Equal(total, saved.Total);
        Assert.Equal(q.WordForTests + "x", saved.Diff.InputText); // 正規化した入力
        Assert.DoesNotContain(q.WordForTests + "x", saved.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(q.WordForTests + "x", saved.Diff.ToString(), StringComparison.OrdinalIgnoreCase);

        shell.Run(env.Time, s => ((FeedbackViewModel)s).NextCommand);
        Assert.Null(shell.LastAnswer);
    }

    // ---- 通知とログ ----

    [Fact]
    public void Notices_from_start_fresh_stay_until_dismissed_and_are_not_duplicated()
    {
        using var env = new ShellEnv();
        env.Dir.Write("progress.json", "{ broken");
        env.Dir.Write("progress.bak.json", "{ broken");
        env.Prompt.Answer = true;
        var shell = env.Start();
        shell.Run(env.Time, s => ((BothCorruptViewModel)s).StartFreshCommand);

        var notice = Assert.Single(shell.Notices);
        shell.StartPractice(env.Time); // 画面が変わっても残る
        Assert.Equal([notice], shell.Notices);

        shell.DismissNotice(notice);
        Assert.Empty(shell.Notices);
    }

    [Fact]
    public void Notices_of_the_screen_after_reload_are_picked_up_once()
    {
        using var env = new ShellEnv();
        env.Dir.Write("settings.json", "{ broken"); // 読めたときの最初の画面に通知が付く
        env.Dir.Write("progress.json", ShellEnvJson.EmptyProgress);
        env.Files.FailReadsOf.Add("progress.json"); // 読めない → 終了のみ(再読込できる。通知は付かない)
        var shell = env.Start();
        Assert.True(shell.As<FatalViewModel>().CanReload);
        Assert.Empty(shell.Notices);

        shell.Run(env.Time, s => ((FatalViewModel)s).ReloadCommand); // まだ読めない
        Assert.IsType<FatalViewModel>(shell.Current);
        env.Files.FailReadsOf.Clear();
        shell.Run(env.Time, s => ((FatalViewModel)s).ReloadCommand); // 読めた

        Assert.IsType<HomeViewModel>(shell.Current);
        Assert.Single(shell.Notices);
    }

    [Fact]
    public void Save_failure_is_logged_with_session_id_and_without_the_input()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        var q = shell.StartPractice(env.Time);
        env.Files.FailWrites = true;
        q.Input = "zzqqinput";

        shell.Run(env.Time, s => ((QuestionViewModel)s).AnswerCommand);

        var line = Assert.Single(env.Log.Lines);
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.NotNull(line.SessionId);
        Assert.NotNull(line.Reason);
        Assert.DoesNotContain("zzqqinput", line.Reason!, StringComparison.Ordinal);
        Assert.DoesNotContain(env.Dir.Path, line.Reason!, StringComparison.Ordinal);
    }
}
