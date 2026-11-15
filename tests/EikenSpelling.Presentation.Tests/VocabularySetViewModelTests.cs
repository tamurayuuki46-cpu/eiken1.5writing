using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;
using EikenSpelling.Flow;
using EikenSpelling.Flow.Tests;
using EikenSpelling.Presentation.Screens;
using EikenSpelling.Presentation.Text;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Presentation.Tests;

/// <summary>w + 数字を a〜j に置いた英字だけの単語(WordSetValidator は英字だけを受け付ける。順序は保たれる)。</summary>
internal static class SetWords
{
    public static string Id(int i) => string.Concat($"w{i:D3}".Select(c => char.IsDigit(c) ? (char)('a' + (c - '0')) : c));

    public static TestWord[] Make(int total, Func<int, bool> hasBlank) =>
        [.. Enumerable.Range(0, total).Select(i => new TestWord(Id(i), Blanks: hasBlank(i) ? "w_" + Id(i)[2..] : null))];
}

/// <summary>語彙セットの画面(S-06・UC-07〜10)。本物の Flow(一時フォルダ)で ViewModel の操作を確かめる。</summary>
public class VocabularySetViewModelTests
{
    /// <summary>120語。セット1のうち穴あきが使えるのは先頭の8語だけ(穴あき固定なら 20問希望 → 8問可能)。</summary>
    private static ShellEnv Env()
    {
        var env = new ShellEnv { WordsJson = Words.Json(SetWords.Make(120, i => i < 8 || i >= 50)) };
        return env;
    }

    private static VocabularySetViewModel OpenSets(ShellEnv env, ShellViewModel shell)
    {
        shell.Run(env.Time, s => ((HomeViewModel)s).OpenVocabularySetsCommand);
        return shell.As<VocabularySetViewModel>();
    }

    private static void ChooseBlank(VocabularySetViewModel set, int count)
    {
        set.SelectedQuestionCount = count;
        set.SelectedMode = set.ModeOptions.First(o => !o.AutoMix && o.Mode == Blank);
    }

    [Fact]
    public void Home_has_a_vocabulary_set_entrance_that_opens_the_first_page_without_writing()
    {
        using var env = Env();
        var shell = env.Start();
        var before = env.Dir.Snapshot();

        var set = OpenSets(env, shell);

        Assert.Equal((50, 1, 3), (set.UnitSize, set.Number, set.Total));
        Assert.Equal(50, set.Rows.Count);
        Assert.Equal(SetWords.Id(0), set.Rows[0].Word);
        Assert.Equal("未出題", set.Rows[0].StateText);
        Assert.Contains("1", set.PageText);
        Assert.Contains("3", set.PageText);
        Assert.Contains("50", set.PageText);
        Assert.Equal(before, env.Dir.Snapshot());
    }

    [Fact]
    public void Paging_and_unit_commands_move_the_page_and_disable_the_edges()
    {
        using var env = Env();
        var shell = env.Start();
        var first = OpenSets(env, shell);
        Assert.False(first.PreviousCommand.CanExecute(null));
        Assert.True(first.NextCommand.CanExecute(null));
        Assert.True(first.IsUnit50Selected);

        shell.Run(env.Time, s => ((VocabularySetViewModel)s).NextCommand);
        shell.Run(env.Time, s => ((VocabularySetViewModel)s).NextCommand);
        var last = shell.As<VocabularySetViewModel>();
        Assert.Equal((3, 20), (last.Number, last.Rows.Count));
        Assert.False(last.NextCommand.CanExecute(null));

        shell.Run(env.Time, s => ((VocabularySetViewModel)s).Unit100Command);
        var hundred = shell.As<VocabularySetViewModel>();
        Assert.Equal((100, 2, 2), (hundred.UnitSize, hundred.Number, hundred.Total)); // 先頭の語(100番目)を含む100語のページ
        Assert.False(hundred.IsUnit50Selected);
        shell.Run(env.Time, s => ((VocabularySetViewModel)s).Unit50Command);
        Assert.Equal(3, shell.As<VocabularySetViewModel>().Number);
    }

    [Fact]
    public void Back_returns_to_home_and_nothing_is_written_by_browsing()
    {
        using var env = Env();
        var shell = env.Start();
        var before = env.Dir.Snapshot();
        OpenSets(env, shell);
        shell.Run(env.Time, s => ((VocabularySetViewModel)s).NextCommand);
        shell.Run(env.Time, s => ((VocabularySetViewModel)s).Unit100Command);

        shell.Run(env.Time, s => ((VocabularySetViewModel)s).BackCommand);

        Assert.IsType<HomeViewModel>(shell.Current);
        Assert.Equal(before, env.Dir.Snapshot());
    }

    [Fact]
    public void Short_candidates_show_desired_and_available_and_confirming_starts_the_same_plan()
    {
        using var env = Env();
        var shell = env.Start();
        var set = OpenSets(env, shell);
        ChooseBlank(set, 20);
        var before = env.Dir.Snapshot();

        shell.Run(env.Time, s => ((VocabularySetViewModel)s).StartCommand);

        var preview = shell.As<SetStartPreviewViewModel>();
        Assert.Equal((20, 8), (preview.Desired, preview.Available));
        Assert.True(preview.CanStart);
        Assert.Contains("8", preview.Headline);
        Assert.Contains("20", preview.Headline);
        Assert.Equal("8問で開始", preview.StartLabel);
        Assert.Equal(before, env.Dir.Snapshot());

        shell.Run(env.Time, s => ((SetStartPreviewViewModel)s).ConfirmCommand);

        var question = shell.As<QuestionViewModel>();
        Assert.Equal(8, question.Total);
    }

    [Fact]
    public void Cancel_returns_to_the_same_page_keeping_the_chosen_conditions_and_writes_nothing()
    {
        using var env = Env();
        var shell = env.Start();
        var set = OpenSets(env, shell);
        ChooseBlank(set, 20);
        set.WeakOnly = false;
        shell.Run(env.Time, s => ((VocabularySetViewModel)s).StartCommand);
        var before = env.Dir.Snapshot();

        shell.Run(env.Time, s => ((SetStartPreviewViewModel)s).CancelCommand);

        var back = shell.As<VocabularySetViewModel>();
        Assert.Equal(1, back.Number);
        Assert.Equal(20, back.SelectedQuestionCount);
        Assert.Equal(Blank, back.SelectedMode.Mode);
        Assert.False(back.SelectedMode.AutoMix);
        Assert.Equal(before, env.Dir.Snapshot());
    }

    [Fact]
    public void Zero_candidates_give_a_reason_and_no_way_to_start()
    {
        using var env = new ShellEnv { WordsJson = Words.Json(SetWords.Make(120, i => i >= 50)) };
        var shell = env.Start();
        var set = OpenSets(env, shell);
        ChooseBlank(set, 10);

        shell.Run(env.Time, s => ((VocabularySetViewModel)s).StartCommand);

        var preview = shell.As<SetStartPreviewViewModel>();
        Assert.False(preview.CanStart);
        Assert.False(preview.ConfirmCommand.CanExecute(null));
        Assert.NotNull(preview.ReasonText);
        Assert.Null(env.ProgressJsonText);
        shell.Run(env.Time, s => ((SetStartPreviewViewModel)s).CancelCommand);
        Assert.IsType<VocabularySetViewModel>(shell.Current);
    }

    [Fact]
    public void Full_count_starts_without_a_confirmation_screen()
    {
        using var env = Env();
        var shell = env.Start();
        var set = OpenSets(env, shell);
        set.SelectedQuestionCount = 5;

        shell.Run(env.Time, s => ((VocabularySetViewModel)s).StartCommand);

        Assert.IsType<QuestionViewModel>(shell.Current);
    }

    [Fact]
    public void Start_save_failure_keeps_the_confirmation_with_a_message_and_retry_starts_one_session()
    {
        using var env = Env();
        var shell = env.Start();
        var set = OpenSets(env, shell);
        ChooseBlank(set, 20);
        shell.Run(env.Time, s => ((VocabularySetViewModel)s).StartCommand);
        env.Files.FailWrites = true;

        shell.Run(env.Time, s => ((SetStartPreviewViewModel)s).ConfirmCommand);

        var failed = shell.As<SetStartPreviewViewModel>();
        Assert.NotNull(failed.SaveFailure);
        Assert.NotEmpty(env.Log.Lines); // 原因はログへ(画面には出さない)
        env.Files.FailWrites = false;

        shell.Run(env.Time, s => ((SetStartPreviewViewModel)s).ConfirmCommand);

        Assert.IsType<QuestionViewModel>(shell.Current);
        Assert.Equal(1, env.Dir.Read(EikenSpelling.Persistence.ProgressRepository.FileName)!.Split("\"sessionId\"").Length - 1);
    }

    [Fact]
    public void An_unusable_mode_cannot_be_started_from_a_set()
    {
        using var env = Env();
        var shell = env.Start();
        var set = OpenSets(env, shell);

        set.SelectedMode = set.ModeOptions.First(o => o.Mode == Audio && !o.Available);

        Assert.False(set.StartCommand.CanExecute(null));
        Assert.NotNull(set.SelectedModeUnavailableNote);
    }

    [Fact]
    public void Every_command_of_the_set_screens_goes_through_the_gate()
    {
        using var env = Env();
        var shell = env.Start();
        var set = OpenSets(env, shell);
        ChooseBlank(set, 20);
        AssertGated(env, shell);

        shell.Run(env.Time, s => ((VocabularySetViewModel)s).StartCommand);
        shell.As<SetStartPreviewViewModel>();
        AssertGated(env, shell);
    }

    private static void AssertGated(ShellEnv env, ShellViewModel shell)
    {
        var screen = shell.Current;
        screen.BeginAccepting();
        var before = env.Dir.Snapshot();
        Assert.NotEmpty(screen.Commands);

        foreach (var command in screen.Commands)
        {
            command.Execute(null);
        }

        Assert.Same(screen, shell.Current);
        Assert.Equal(before, env.Dir.Snapshot());
    }

    [Fact]
    public void Resume_prompt_names_the_set_the_practice_was_started_from()
    {
        using var env = Env();
        var shell = env.Start();
        var set = OpenSets(env, shell);
        ChooseBlank(set, 20);
        shell.Run(env.Time, s => ((VocabularySetViewModel)s).StartCommand);
        shell.Run(env.Time, s => ((SetStartPreviewViewModel)s).ConfirmCommand);

        var restarted = env.Start();

        var prompt = restarted.As<ResumePromptViewModel>();
        Assert.NotNull(prompt.SetText);
        Assert.Contains("1", prompt.SetText);
        Assert.Contains("3", prompt.SetText);
        Assert.Null(prompt.SetUpdatedNote);
    }

    [Fact]
    public void Resume_prompt_of_a_normal_practice_has_no_set_text()
    {
        using var env = Env();
        var shell = env.Start();
        shell.StartPractice(env.Time, 5);

        var prompt = env.Start().As<ResumePromptViewModel>();

        Assert.Null(prompt.SetText);
    }

    [Fact]
    public void Text_formats_cover_every_state_and_the_new_empty_reason()
    {
        foreach (var state in Enum.GetValues<WordStudyState>())
        {
            Assert.False(string.IsNullOrWhiteSpace(ScreenText.StudyState(state)));
        }

        Assert.NotNull(ScreenText.EmptyReason(SelectionEmptyReason.EmptyScope));
    }

    [Fact]
    public void Cancel_from_the_confirmation_puts_focus_near_start_and_paging_keeps_the_pressed_button()
    {
        using var env = Env();
        var shell = env.Start();
        var set = OpenSets(env, shell);
        Assert.Equal(VocabularySetFocus.Unit, set.InitialFocus); // 開いた直後の Enter で練習が始まらない
        ChooseBlank(set, 20);
        shell.Run(env.Time, s => ((VocabularySetViewModel)s).StartCommand);

        shell.Run(env.Time, s => ((SetStartPreviewViewModel)s).CancelCommand);
        Assert.Equal(VocabularySetFocus.Start, shell.As<VocabularySetViewModel>().InitialFocus);

        shell.Run(env.Time, s => ((VocabularySetViewModel)s).NextCommand);
        Assert.Equal(VocabularySetFocus.Next, shell.As<VocabularySetViewModel>().InitialFocus);
    }

    private static string StartFiveFromSetOne(ShellEnv env)
    {
        var shell = env.Start();
        var set = OpenSets(env, shell);
        set.SelectedQuestionCount = 5;
        shell.Run(env.Time, s => ((VocabularySetViewModel)s).StartCommand);
        var session = System.Text.Json.Nodes.JsonNode.Parse(env.ProgressJsonText!)!["session"]!;
        return (string)session["questions"]![0]!["wordId"]!;
    }

    [Fact]
    public void Resume_prompt_after_a_word_update_explains_the_plan_is_kept_when_it_can_resume()
    {
        using var env = Env();
        StartFiveFromSetOne(env);
        env.WordsJson = Words.Json("v2", SetWords.Make(120, i => i < 8 || i >= 50)); // 版だけ変わる

        var prompt = env.Start().As<ResumePromptViewModel>();

        Assert.True(prompt.CanResume);
        Assert.NotNull(prompt.SetUpdatedNote);
    }

    [Fact]
    public void Resume_prompt_does_not_say_the_practice_can_continue_when_a_planned_word_is_gone()
    {
        using var env = Env();
        var planned = StartFiveFromSetOne(env);
        env.WordsJson = Words.Json("v2", [.. SetWords.Make(120, i => i < 8 || i >= 50).Where(w => w.Id != planned)]);

        var prompt = env.Start().As<ResumePromptViewModel>();

        Assert.False(prompt.CanResume);
        Assert.NotNull(prompt.CannotResumeReason);
        Assert.Null(prompt.SetUpdatedNote); // 「続けられます」とは言わない
    }
}
