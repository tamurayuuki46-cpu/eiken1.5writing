using EikenSpelling.Flow;
using EikenSpelling.Presentation.Screens;

namespace EikenSpelling.Presentation;

/// <summary>Flow の画面の状態の型 → 画面の ViewModel(design 4.3 の表)。判断はしない。</summary>
internal static class ScreenViewModelFactory
{
    public static ScreenViewModel Create(ShellViewModel shell, FlowScreen screen, ScreenViewModel? previous) => screen switch
    {
        // 0語・開始の保存失敗でホームに戻ったときは、直前に選んでいた値を残す
        HomeScreen home => new HomeViewModel(shell, home, (previous as HomeViewModel)?.Choice),
        ResumePromptScreen resume => new ResumePromptViewModel(shell, resume),
        QuestionScreen question => new QuestionViewModel(shell, question),
        FeedbackScreen feedback => new FeedbackViewModel(
            shell, feedback, shell.LastAnswer ?? throw new InvalidOperationException("判定画面に出す直前の回答の値がありません。")),
        ResultScreen result => new ResultViewModel(shell, result),
        BothCorruptScreen corrupt => new BothCorruptViewModel(shell, corrupt),
        FatalScreen fatal => new FatalViewModel(shell, fatal),
        _ => throw new InvalidOperationException($"この画面にはまだ対応していません: {screen.GetType().Name}"),
    };
}
