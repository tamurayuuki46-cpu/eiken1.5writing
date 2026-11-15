using EikenSpelling.Core.Application;
using EikenSpelling.Flow;
using EikenSpelling.Presentation.Screens;
using EikenSpelling.Presentation.Text;

namespace EikenSpelling.Presentation.Tests;

/// <summary>ロードマップ②: 答えを見る・結果 → 苦手一覧 → 行内詳細 → 戻る・一括復習(UC-05/06/19/23、AC-R04/R10、AC-X09)。</summary>
public class ResultWeakReviewViewModelTests
{
    /// <summary>3問(日本語のみ)で 1問目を後回し → 2問目 正解 → 3問目 誤答 → 後回しの1問目の画面にする。</summary>
    private static QuestionViewModel AtDeferred(ShellEnv env, ShellViewModel shell)
    {
        shell.StartPractice(env.Time, count: 3);
        shell.Run(env.Time, s => ((QuestionViewModel)s).SkipCommand);
        shell.AnswerCurrent(env.Time, correct: true);
        shell.Run(env.Time, s => ((FeedbackViewModel)s).NextCommand);
        shell.AnswerCurrent(env.Time, correct: false);
        shell.Run(env.Time, s => ((FeedbackViewModel)s).NextCommand);
        var q = shell.As<QuestionViewModel>();
        Assert.Equal(QuestionPhase.Deferred, q.Phase);
        return q;
    }

    private static ResultViewModel FinishToResult(ShellEnv env, ShellViewModel shell)
    {
        var deferred = AtDeferred(env, shell);
        Assert.True(deferred.RevealCommand.CanExecute(null));
        shell.Run(env.Time, s => ((QuestionViewModel)s).RevealCommand);
        shell.Run(env.Time, s => ((FeedbackViewModel)s).NextCommand);
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

        return shell.As<ResultViewModel>();
    }

    // ---- 答えを見る ----

    [Fact]
    public void Reveal_is_offered_only_in_deferred_and_shows_result_without_a_diff_or_input()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        var first = shell.StartPractice(env.Time, count: 3);
        Assert.False(first.CanReveal); // 通常の段階では出さない(後回しか回答)
        Assert.False(first.RevealCommand.CanExecute(null));
        shell.Run(env.Time, s => ((QuestionViewModel)s).SkipCommand);
        shell.AnswerCurrent(env.Time, correct: true);
        shell.Run(env.Time, s => ((FeedbackViewModel)s).NextCommand);
        shell.AnswerCurrent(env.Time, correct: false);
        shell.Run(env.Time, s => ((FeedbackViewModel)s).NextCommand);
        var deferred = shell.As<QuestionViewModel>();
        Assert.True(deferred.CanReveal);
        Assert.True(deferred.IsDeferred);
        Assert.False(deferred.SkipCommand.CanExecute(null));
        var word = deferred.WordForTests;

        shell.Run(env.Time, s => ((QuestionViewModel)s).RevealCommand);

        var feedback = shell.As<FeedbackViewModel>();
        Assert.True(feedback.IsRevealed);
        Assert.False(feedback.IsCorrect);
        Assert.Equal(ScreenText.RevealedResult, feedback.ResultText);
        Assert.Equal(word, feedback.CorrectWord);
        Assert.Empty(feedback.Diff.Messages);
        Assert.Empty(feedback.Diff.InputLetters);
        Assert.Equal("", feedback.Diff.InputText);
        Assert.Equal("後回し:全1問中の1問目", feedback.ProgressText);
        Assert.True(feedback.Saved);
    }

    [Fact]
    public void Reveal_is_not_offered_in_the_retry_stage()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        shell.StartPractice(env.Time, count: 2);
        shell.AnswerCurrent(env.Time, correct: false);
        shell.Run(env.Time, s => ((FeedbackViewModel)s).NextCommand);
        shell.AnswerCurrent(env.Time, correct: true);
        shell.Run(env.Time, s => ((FeedbackViewModel)s).NextCommand);

        var retry = shell.As<QuestionViewModel>();

        Assert.Equal(QuestionPhase.Retry, retry.Phase);
        Assert.False(retry.CanReveal);
        Assert.False(retry.CanSkip);
        Assert.False(retry.RevealCommand.CanExecute(null));
        Assert.False(retry.SkipCommand.CanExecute(null));
    }

    [Fact]
    public void Reveal_with_failed_save_blocks_next_until_resaved_and_keeps_one_wrong()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        AtDeferred(env, shell);
        env.Files.FailWrites = true;

        shell.Run(env.Time, s => ((QuestionViewModel)s).RevealCommand);

        var failed = shell.As<FeedbackViewModel>();
        Assert.True(failed.IsRevealed);
        Assert.False(failed.Saved);
        Assert.NotNull(failed.SaveFailure);
        Assert.False(failed.NextCommand.CanExecute(null));
        Assert.True(failed.RetrySaveCommand.CanExecute(null));
        Assert.True(shell.HasUnsavedAnswer);

        env.Files.FailWrites = false;
        shell.Run(env.Time, s => ((FeedbackViewModel)s).RetrySaveCommand);

        var saved = shell.As<FeedbackViewModel>();
        Assert.True(saved.Saved);
        Assert.True(saved.IsRevealed);
        Assert.True(saved.NextCommand.CanExecute(null));
    }

    // ---- 結果 → 苦手一覧 → 戻る ----

    [Fact]
    public void Result_opens_weak_list_expands_a_row_and_back_returns_the_same_result_without_writing()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        var result = FinishToResult(env, shell);
        var (correct, count) = (result.NormalCorrect, result.NormalCount);
        Assert.Equal((1, 3), (correct, count));
        Assert.Contains(result.MistakenWords, w => w.Contains(ScreenText.RevealedMark, StringComparison.Ordinal));
        var before = env.Dir.Snapshot();

        shell.Run(env.Time, s => ((ResultViewModel)s).OpenWeakListCommand);

        var list = shell.As<WeakListViewModel>();
        Assert.Equal(WeakListOrigin.Result, list.Origin);
        Assert.Equal(ScreenText.BackToResult, list.BackLabel);
        Assert.NotEmpty(list.Rows);
        Assert.All(list.Rows, r => Assert.False(r.IsExpanded));
        var revealedRow = list.Rows.Single(r => !r.HasTypedInput); // 答えを見た語は実入力なし
        Assert.Equal(ScreenText.NoTypedInput, revealedRow.TypedInput);
        Assert.Equal("", revealedRow.TypedInputMeta);
        var typedRow = list.Rows.Single(r => r.HasTypedInput);
        Assert.Equal(typedRow.Word + "x", typedRow.TypedInput); // 誤答の入力(正解語 + x)
        Assert.Contains("日本語", typedRow.TypedInputMeta, StringComparison.Ordinal);
        Assert.NotEqual(revealedRow.Word, typedRow.Word);

        shell.Run(env.Time, _ => revealedRow.ToggleCommand);
        Assert.True(list.Rows.Single(r => r.Word == revealedRow.Word).IsExpanded);
        Assert.Equal(ScreenText.CloseDetail, revealedRow.ToggleLabel);
        Assert.Equal(4, revealedRow.ModeLines.Count);
        Assert.NotEmpty(revealedRow.StatusText);
        Assert.Same(list, shell.Current); // 展開しても画面は替わらない
        Assert.Equal(before, env.Dir.Snapshot());

        shell.Run(env.Time, s => ((WeakListViewModel)s).BackCommand);

        var back = shell.As<ResultViewModel>();
        Assert.Equal((1, 3), (back.NormalCorrect, back.NormalCount));
        Assert.Equal(before, env.Dir.Snapshot());
        Assert.Null(ProgressSession(env)); // 完了したセッションは復活しない
    }

    [Fact]
    public void Home_opens_weak_list_and_back_returns_to_home()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        FinishToResult(env, shell);
        shell.Run(env.Time, s => ((ResultViewModel)s).GoHomeCommand);
        var before = env.Dir.Snapshot();

        shell.Run(env.Time, s => ((HomeViewModel)s).OpenWeakListCommand);
        var list = shell.As<WeakListViewModel>();
        Assert.Equal(WeakListOrigin.Home, list.Origin);
        Assert.Equal(ScreenText.BackToHome, list.BackLabel);
        shell.Run(env.Time, s => ((WeakListViewModel)s).BackCommand);

        Assert.IsType<HomeViewModel>(shell.Current);
        Assert.Equal(before, env.Dir.Snapshot());
    }

    // ---- 一括復習 ----

    [Fact]
    public void Bulk_review_starts_weak_only_practice_from_the_list()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        FinishToResult(env, shell);
        shell.Run(env.Time, s => ((ResultViewModel)s).OpenWeakListCommand);

        shell.Run(env.Time, s => ((WeakListViewModel)s).ReviewCommand);

        var question = shell.As<QuestionViewModel>();
        Assert.Equal(QuestionPhase.Normal, question.Phase);
        Assert.Equal(1, question.Number);
        Assert.True(question.Total <= 2); // 苦手は2語(後回しで答えを見た語と誤答した語)
    }

    [Fact]
    public void Bulk_review_with_no_target_shows_the_reason_and_can_go_back()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        shell.StartPractice(env.Time, count: 2);
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

        shell.Run(env.Time, s => ((ResultViewModel)s).OpenWeakListCommand);
        var empty = shell.As<WeakListViewModel>();
        Assert.True(empty.IsEmpty);
        Assert.Equal(ScreenText.NoWeakWordsInList, empty.EmptyText);
        Assert.False(empty.HasReviewTargets);
        Assert.False(empty.ReviewCommand.CanExecute(null)); // 0件は開始操作不可(UC-30)
        Assert.Equal(ScreenText.NoReviewTargets, empty.ReviewUnavailableText); // 理由は常に出す
        var before = env.Dir.Snapshot();

        shell.Run(env.Time, s => ((WeakListViewModel)s).ReviewCommand);

        Assert.Same(empty, shell.Current);
        Assert.Equal(before, env.Dir.Snapshot());
        shell.Run(env.Time, s => ((WeakListViewModel)s).BackCommand);
        Assert.IsType<ResultViewModel>(shell.Current);
    }

    [Fact]
    public void Sorting_keeps_expanded_rows_and_returns_focus_to_the_pressed_button_without_writing()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        FinishToResult(env, shell);
        shell.Run(env.Time, s => ((ResultViewModel)s).OpenWeakListCommand);
        var list = shell.As<WeakListViewModel>();
        Assert.Equal(WeakListFocus.SortWeakFirst, list.InitialFocus); // 開いた直後は並べ替えに(Enter で復習が始まらない)
        Assert.True(list.HasReviewTargets);
        Assert.Contains("選択中", list.SortWeakFirstLabel, StringComparison.Ordinal);
        var opened = list.Rows[0];
        shell.Run(env.Time, _ => opened.ToggleCommand);
        var before = env.Dir.Snapshot();

        shell.Run(env.Time, s => ((WeakListViewModel)s).SortRecentCommand);

        var sorted = shell.As<WeakListViewModel>();
        Assert.NotSame(list, sorted);
        Assert.Equal(WeakListFocus.SortRecent, sorted.InitialFocus);
        Assert.Contains("選択中", sorted.SortRecentLabel, StringComparison.Ordinal);
        Assert.DoesNotContain("選択中", sorted.SortWeakFirstLabel, StringComparison.Ordinal);
        Assert.True(sorted.Rows.Single(r => r.WordId == opened.WordId).IsExpanded);
        Assert.Equal(WeakListOrigin.Result, sorted.Origin);
        Assert.Equal(before, env.Dir.Snapshot());

        shell.Run(env.Time, s => ((WeakListViewModel)s).BackCommand);
        Assert.True(shell.As<ResultViewModel>().ReturnedFromWeakList); // 結果では「苦手一覧」ボタンにフォーカスを戻す
    }

    [Fact]
    public void Every_command_of_the_weak_list_goes_through_the_gate()
    {
        using var env = new ShellEnv();
        env.Prompt.Answer = true;
        var shell = env.Start();
        FinishToResult(env, shell);
        shell.Run(env.Time, s => ((ResultViewModel)s).OpenWeakListCommand);
        var list = shell.As<WeakListViewModel>();
        list.BeginAccepting(); // ゲートはまだ閉じている(0.3 秒未満)
        var before = env.Dir.Snapshot();

        foreach (var command in list.Commands)
        {
            command.Execute(null);
        }

        Assert.Same(list, shell.Current);
        Assert.All(list.Rows, r => Assert.False(r.IsExpanded));
        Assert.Equal(before, env.Dir.Snapshot());
    }

    private static PracticeSession? ProgressSession(ShellEnv env) => ProgressJson.Parse(env.ProgressJsonText!).Session;
}
