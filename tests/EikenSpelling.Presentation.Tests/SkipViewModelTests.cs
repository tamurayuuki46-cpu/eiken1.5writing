using EikenSpelling.Core.Application;
using EikenSpelling.Presentation.Screens;

namespace EikenSpelling.Presentation.Tests;

/// <summary>出題画面の「後回し」と、その保存失敗(UI v0.3 S-03、AC-X08)。操作可否は Flow の値をそのまま写す。</summary>
public class SkipViewModelTests
{
    [Fact]
    public void Skip_save_failure_keeps_question_and_input_and_allows_only_retry_save()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        var first = shell.StartPractice(env.Time, count: 3);
        Assert.True(first.CanSkip);
        Assert.True(first.SkipCommand.CanExecute(null));
        Assert.Equal("通常", first.PhaseName);
        first.Input = "abc";
        env.Files.FailWrites = true;

        shell.Run(env.Time, s => ((QuestionViewModel)s).SkipCommand);

        var failed = shell.As<QuestionViewModel>();
        Assert.NotSame(first, failed);
        Assert.Equal(first.WordForTests, failed.WordForTests);
        Assert.Equal("abc", failed.Input); // 打ちかけの入力を残す
        Assert.NotNull(failed.SkipSaveFailure);
        Assert.False(failed.AnswerCommand.CanExecute(null));
        Assert.False(failed.SkipCommand.CanExecute(null));
        Assert.False(failed.QuitCommand.CanExecute(null));
        Assert.True(failed.IsNormalPhase); // 後回しボタンは出したまま押せない
        Assert.Contains("もう一度保存", failed.KeyHint, StringComparison.Ordinal);
        Assert.True(failed.RetrySaveCommand.CanExecute(null));
        Assert.True(shell.HasUnsavedAnswer);
        Assert.Contains(env.Log.Lines, l => l.Op == "QuestionScreen" && l.Reason is not null && l.SessionId is not null);
        Assert.DoesNotContain(env.Log.Lines, l => (l.Reason ?? "").Contains("abc", StringComparison.Ordinal));

        // 古い画面(スキップ前)の後回しは何もしない
        first.SkipCommand.Execute(null);
        Assert.Same(failed, shell.Current);

        env.Files.FailWrites = false;
        shell.Run(env.Time, s => ((QuestionViewModel)s).RetrySaveCommand);

        var next = shell.As<QuestionViewModel>();
        Assert.NotEqual(first.WordForTests, next.WordForTests);
        Assert.Null(next.SkipSaveFailure);
        Assert.Equal("", next.Input);
        Assert.Equal(2, next.Number);
        Assert.Equal("通常:全3問中の2問目", next.ProgressText);
        Assert.Equal("Enter で回答 ・ Esc でやめる", next.KeyHint);
        Assert.False(shell.HasUnsavedAnswer);
    }

    [Fact]
    public void Deferred_question_shows_phase_and_has_no_skip()
    {
        using var env = new ShellEnv();
        var shell = env.Start();
        shell.StartPractice(env.Time, count: 2);
        shell.Run(env.Time, s => ((QuestionViewModel)s).SkipCommand);
        shell.AnswerCurrent(env.Time, correct: true);
        shell.Run(env.Time, s => ((FeedbackViewModel)s).NextCommand);

        var deferred = shell.As<QuestionViewModel>();

        Assert.Equal(QuestionPhase.Deferred, deferred.Phase);
        Assert.Equal("後回し", deferred.PhaseName);
        Assert.True(deferred.IsDeferred);
        Assert.False(deferred.CanSkip);
        Assert.False(deferred.SkipCommand.CanExecute(null));
        Assert.Equal((1, 1), (deferred.Number, deferred.Total));
        Assert.Equal("後回し:全1問中の1問目", deferred.ProgressText);
        Assert.Equal(0.5, deferred.Progress); // 練習全体: 2問中1問を採点済み(段階が変わっても巻き戻らない)
        Assert.False(deferred.IsNormalPhase);
    }
}
