using EikenSpelling.Core.Domain;
using EikenSpelling.Flow.Tests;
using EikenSpelling.Persistence;
using EikenSpelling.Presentation.Screens;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Presentation.Tests;

/// <summary>各画面の ViewModel(design 4.3〜4.5、requirements R6〜R18)。</summary>
public class ScreenViewModelTests
{
    // ---- ホーム ----

    [Fact]
    public void Home_shows_status_and_question_count_choices_and_defaults()
    {
        using var env = new ShellEnv();
        var home = env.Start().As<HomeViewModel>();

        Assert.Equal(5, home.Unseen);
        Assert.Equal(0, home.Weak);
        Assert.Equal([5, 10, 20], home.QuestionCountOptions);
        Assert.Equal(10, home.SelectedQuestionCount);
        Assert.True(home.SelectedMode.AutoMix);
        Assert.Equal(["自動ミックス", "日本語", "穴あき"], home.ModeOptions.Select(o => o.Label));
        Assert.Null(home.InfoMessage);
        Assert.Null(home.WarningMessage);
    }

    [Fact]
    public void Home_keeps_the_previous_choice_when_no_word_can_be_practiced()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        var home = shell.As<HomeViewModel>();
        home.WeakOnly = true;
        home.SelectedQuestionCount = 20;

        shell.Run(env.Time, s => ((HomeViewModel)s).StartCommand);

        var again = shell.As<HomeViewModel>();
        Assert.NotSame(home, again);
        Assert.NotNull(again.InfoMessage);
        Assert.True(again.WeakOnly);
        Assert.Equal(20, again.SelectedQuestionCount);
        Assert.Null(env.ProgressJsonText);
    }

    [Fact]
    public void Home_shows_a_warning_when_the_start_cannot_be_saved()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        env.Files.FailWrites = true;

        shell.Run(env.Time, s => ((HomeViewModel)s).StartCommand);

        var home = shell.As<HomeViewModel>();
        Assert.NotNull(home.WarningMessage);
        Assert.Null(home.InfoMessage);
    }

    [Fact]
    public void Home_shows_saved_values_that_are_not_in_the_choices_and_an_unavailable_manual_mode()
    {
        using var env = new ShellEnv();
        env.Dir.Write(SettingsRepository.FileName, """{ "settingsVersion": 1, "practice": { "questionCount": 7, "autoMix": false, "manualMode": "Audio" } }""");
        var shell = env.Start();
        var home = shell.As<HomeViewModel>();

        Assert.Equal([5, 7, 10, 20], home.QuestionCountOptions);
        Assert.Equal(7, home.SelectedQuestionCount);
        Assert.Equal(Audio, home.SelectedMode.Mode);
        Assert.False(home.SelectedMode.Available);
        Assert.Contains("使えません", home.SelectedMode.Label, StringComparison.Ordinal);

        shell.Run(env.Time, s => ((HomeViewModel)s).StartCommand);
        Assert.NotNull(shell.As<HomeViewModel>().InfoMessage); // ManualModeUnavailable の理由
        Assert.Null(env.ProgressJsonText);
    }

    // ---- 出題 ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Blank_answers_are_disabled_and_send_nothing(string input)
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        var q = shell.StartPractice(env.Time);
        var before = env.ProgressJsonText;

        q.Input = input;
        Assert.True(q.IsInputBlank);
        Assert.False(q.AnswerCommand.CanExecute(null));
        shell.Run(env.Time, s => ((QuestionViewModel)s).AnswerCommand);

        Assert.Same(q, shell.Current);
        Assert.Equal(before, env.ProgressJsonText);

        q.Input = "a";
        Assert.True(q.AnswerCommand.CanExecute(null));
    }

    [Fact]
    public void Blank_mode_shows_the_blanked_word_and_the_letter_count()
    {
        using var env = new ShellEnv { WordsJson = Words.Json(new TestWord("receive", Blanks: "rec__ve")) };
        var shell = env.Start();
        var home = shell.As<HomeViewModel>();
        home.SelectedMode = home.ModeOptions.Single(o => !o.AutoMix && o.Mode == Blank);

        var q = shell.StartPractice(env.Time, count: 1);

        Assert.True(q.IsBlankMode);
        Assert.Equal("rec__ve", q.Prompt);
        Assert.Equal(7, q.LetterCount);
        Assert.Equal("穴あき", q.ModeName);
    }

    [Fact]
    public void Quit_asks_first_and_stays_on_no_and_goes_to_the_resume_prompt_on_yes()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        var q = shell.StartPractice(env.Time);

        env.Prompt.Answer = false;
        shell.Run(env.Time, s => ((QuestionViewModel)s).QuitCommand);
        Assert.Same(q, shell.Current);
        Assert.Equal([QuestionViewModel.QuitConfirmMessage], env.Prompt.Asked);

        env.Prompt.Answer = true;
        shell.Run(env.Time, s => ((QuestionViewModel)s).QuitCommand);
        Assert.IsType<ResumePromptViewModel>(shell.Current);
    }

    [Fact]
    public void Retry_question_is_marked_and_explains_the_grown_total_and_the_last_button_is_results()
    {
        using var env = new ShellEnv { WordsJson = Words.Json(new TestWord("receive")) };
        var shell = env.Start();
        var first = shell.StartPractice(env.Time, count: 1);
        Assert.False(first.IsRetry);
        Assert.Null(first.RetryNote);
        Assert.Equal(1, first.Total);

        var wrong = shell.AnswerCurrent(env.Time, correct: false);
        Assert.False(wrong.IsLast); // 最後の通常問題を誤答すると再練習が足される
        Assert.Equal("次へ", wrong.NextLabel);
        Assert.Equal(1, wrong.Total); // 判定画面は直前の出題の値のまま

        shell.Run(env.Time, s => ((FeedbackViewModel)s).NextCommand);
        var retry = shell.As<QuestionViewModel>();
        Assert.True(retry.IsRetry);
        Assert.NotNull(retry.RetryNote);
        Assert.Equal(2, retry.Total);

        var last = shell.AnswerCurrent(env.Time, correct: true);
        Assert.True(last.IsLast);
        Assert.Equal("結果を見る", last.NextLabel);
    }

    // ---- 判定 ----

    [Fact]
    public void Feedback_uses_core_describe_and_normalized_input_positions()
    {
        using var env = new ShellEnv { WordsJson = Words.Json(new TestWord("receive")) };
        var shell = env.Start();
        var q = shell.StartPractice(env.Time, count: 1);
        q.Input = " Recieve";
        shell.Run(env.Time, s => ((QuestionViewModel)s).AnswerCommand);

        var feedback = shell.As<FeedbackViewModel>();
        var expected = SpellingDiffService.Diff("receive", " Recieve");
        Assert.False(feedback.IsCorrect);
        Assert.Equal("不正解", feedback.ResultText);
        Assert.Equal(expected.Select(SpellingDiffService.Describe), feedback.Diff.Messages);
        Assert.Null(feedback.Diff.MoreText);
        Assert.Equal("recieve", feedback.Diff.InputText);
        Assert.Equal("receive", new string([.. feedback.Diff.CorrectLetters.Select(l => l.Character)]));
        Assert.Equal([4, 5], feedback.Diff.CorrectLetters.Select((l, i) => (l, i)).Where(x => x.l.Marked).Select(x => x.i + 1));
        Assert.Contains("苦手に登録されました", feedback.Changes);
        Assert.True(feedback.Saved);
    }

    [Fact]
    public void Extra_letters_are_struck_on_the_normalized_input()
    {
        using var env = new ShellEnv { WordsJson = Words.Json(new TestWord("until")) };
        var shell = env.Start();
        var q = shell.StartPractice(env.Time, count: 1);
        q.Input = "  UNTILL";
        shell.Run(env.Time, s => ((QuestionViewModel)s).AnswerCommand);

        var diff = shell.As<FeedbackViewModel>().Diff;
        Assert.Equal("untill", diff.InputText);
        var struck = diff.InputLetters.Select((l, i) => (l, i)).Where(x => x.l.Marked).Select(x => x.i + 1).ToArray();
        Assert.Equal(SpellingDiffService.Diff("until", "  UNTILL").Where(d => d.Kind == DiffKind.Extra).Select(d => d.InputPosition), struck);
        Assert.DoesNotContain(diff.CorrectLetters, l => l.Marked);
    }

    [Theory]
    [InlineData("rexeive", 1, null)] // 1か所
    [InlineData("rxxxive", 3, null)] // ちょうど3か所
    [InlineData("rxxxxve", 3, "ほか 1 か所")] // 4か所
    public void More_than_three_differences_are_summarized(string input, int messages, string? more)
    {
        using var env = new ShellEnv { WordsJson = Words.Json(new TestWord("receive")) };
        var shell = env.Start();
        var q = shell.StartPractice(env.Time, count: 1);
        q.Input = input;
        shell.Run(env.Time, s => ((QuestionViewModel)s).AnswerCommand);

        var diff = shell.As<FeedbackViewModel>().Diff;
        Assert.Equal(messages, diff.Messages.Count);
        Assert.Equal(more, diff.MoreText);
    }

    [Fact]
    public void Save_failure_disables_next_and_enables_retry_save_until_saved()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        shell.StartPractice(env.Time);
        env.Files.FailWrites = true;

        var failed = shell.AnswerCurrent(env.Time, correct: true);

        Assert.False(failed.Saved);
        Assert.NotNull(failed.SaveFailure);
        Assert.False(failed.NextCommand.CanExecute(null));
        Assert.True(failed.RetrySaveCommand.CanExecute(null));
        Assert.True(shell.HasUnsavedAnswer);
        shell.Run(env.Time, s => ((FeedbackViewModel)s).NextCommand);
        Assert.Same(failed, shell.Current); // 次へは何もしない

        env.Files.FailWrites = false;
        shell.Run(env.Time, s => ((FeedbackViewModel)s).RetrySaveCommand);
        var saved = shell.As<FeedbackViewModel>();
        Assert.True(saved.NextCommand.CanExecute(null));
        Assert.False(saved.RetrySaveCommand.CanExecute(null));
        Assert.False(shell.HasUnsavedAnswer);
    }

    // ---- 結果 ----

    [Fact]
    public void Result_shows_counts_mistakes_retries_and_state_changes()
    {
        using var env = new ShellEnv { WordsJson = Words.Json(new TestWord("receive"), new TestWord("until")) };
        var shell = env.Start();
        shell.StartPractice(env.Time, count: 2);
        var wrongWord = shell.As<QuestionViewModel>().WordForTests;
        shell.AnswerCurrent(env.Time, correct: false);
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

        var result = shell.As<ResultViewModel>();
        Assert.Equal(1, result.NormalCorrect);
        Assert.Equal(2, result.NormalCount);
        Assert.Equal([wrongWord], result.MistakenWords);
        var retry = Assert.Single(result.Retries);
        Assert.True(retry.FinallyCorrect);
        Assert.Equal(["苦手に登録", "克服", "苦手に戻った"], result.ChangeGroups.Select(g => g.Title));
        Assert.Equal(wrongWord, result.ChangeGroups[0].WordsText);
        Assert.Equal("なし", result.ChangeGroups[1].WordsText);

        shell.Run(env.Time, s => ((ResultViewModel)s).GoHomeCommand);
        Assert.Equal(1, shell.As<HomeViewModel>().Weak);
    }

    // ---- 再開の選択 ----

    [Fact]
    public void Restart_mid_practice_offers_resume_and_continues_from_the_next_number()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        shell.StartPractice(env.Time);
        shell.AnswerCurrent(env.Time, correct: true);

        var restarted = env.Start(); // 閉じて起動し直す
        var prompt = restarted.As<ResumePromptViewModel>();
        Assert.Equal(1, prompt.Answered);
        Assert.Equal(5, prompt.Total);
        Assert.True(prompt.CanResume);
        Assert.Equal("11月1日 10:00", prompt.StartedAtText);

        restarted.Run(env.Time, s => ((ResumePromptViewModel)s).ResumeCommand);
        Assert.Equal(2, restarted.As<QuestionViewModel>().Number);
    }

    [Fact]
    public void Discard_failure_keeps_the_resume_prompt_with_a_message()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        shell.StartPractice(env.Time);
        var restarted = env.Start();
        env.Files.FailWrites = true;

        restarted.Run(env.Time, s => ((ResumePromptViewModel)s).DiscardCommand);

        Assert.NotNull(restarted.As<ResumePromptViewModel>().Message);
        env.Files.FailWrites = false;
        restarted.Run(env.Time, s => ((ResumePromptViewModel)s).DiscardCommand);
        // 1回だけ落ちたことがある(tasklist のメモ)。落ちたときに原因の文言が分かるようにする
        Assert.True(restarted.Current is HomeViewModel, $"ホームにならない: {restarted.Current.GetType().Name} / {(restarted.Current as ResumePromptViewModel)?.Message}");
    }

    [Fact]
    public void Resume_is_not_offered_when_the_remaining_words_are_gone_from_the_word_data()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        shell.StartPractice(env.Time); // 5語すべてが出題される
        env.WordsJson = Words.Json(Words.Five[0]); // 単語データから4語が消えた

        var prompt = env.Start().As<ResumePromptViewModel>();

        Assert.False(prompt.CanResume);
        Assert.False(prompt.ResumeCommand.CanExecute(null));
        Assert.False(string.IsNullOrEmpty(prompt.CannotResumeReason));
        Assert.True(prompt.DiscardCommand.CanExecute(null));
    }

    // ---- 両方破損・終了のみ ----

    [Fact]
    public void Both_corrupt_writes_nothing_on_no_and_exit_requests_exit()
    {
        using var env = new ShellEnv();
        env.Dir.Write(ProgressRepository.FileName, "{ broken");
        env.Dir.Write("progress.bak.json", "{ broken");
        var shell = env.Start();
        var snapshot = env.Dir.Snapshot();
        var exits = 0;
        shell.ExitRequested += (_, _) => exits++;

        env.Prompt.Answer = false;
        shell.Run(env.Time, s => ((BothCorruptViewModel)s).StartFreshCommand);
        Assert.IsType<BothCorruptViewModel>(shell.Current);
        Assert.Equal(snapshot, env.Dir.Snapshot());
        Assert.Equal([BothCorruptViewModel.StartFreshConfirmMessage], env.Prompt.Asked);

        shell.Run(env.Time, s => ((BothCorruptViewModel)s).ExitCommand);
        Assert.Equal(1, exits);
        Assert.Equal(snapshot, env.Dir.Snapshot());
    }

    [Fact]
    public void Fatal_for_a_newer_version_offers_only_exit()
    {
        using var env = new ShellEnv();
        env.Dir.Write(ProgressRepository.FileName, """{ "schemaVersion": 99 }""");
        var fatal = env.Start().As<FatalViewModel>();

        Assert.False(fatal.CanReload);
        Assert.False(fatal.ReloadCommand.CanExecute(null));
        Assert.True(fatal.ExitCommand.CanExecute(null));
    }

    [Fact]
    public void Relative_data_directory_is_a_fatal_folder_screen()
    {
        using var env = new ShellEnv { DataDirectory = "relative\\dir" };

        var fatal = env.Start().As<FatalViewModel>();

        Assert.Contains("保存先", fatal.Reason, StringComparison.Ordinal);
        Assert.Empty(env.Dir.Names());
    }
}
