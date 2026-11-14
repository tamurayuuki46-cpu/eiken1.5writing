using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;
using EikenSpelling.Flow;
using EikenSpelling.Presentation.Mvvm;
using EikenSpelling.Presentation.Text;

namespace EikenSpelling.Presentation.Screens;

/// <summary>出題(R8〜R10・R14)。空欄・空白だけの入力は送らない(判定は Core の SpellingJudge.IsBlank)。</summary>
public sealed class QuestionViewModel : ScreenViewModel
{
    public const string QuitConfirmMessage = "練習をやめますか?(ここまでの回答は保存されています。次の起動か「続きから」で再開できます)";

    private readonly string _word;
    private string _input = "";

    public QuestionViewModel(ShellViewModel shell, QuestionScreen screen)
        : base(shell)
    {
        var q = screen.Question;
        _word = q.Word;
        Number = screen.Number;
        Total = screen.Total;
        Mode = q.Mode;
        ModeName = ScreenText.ModeName(q.Mode);
        Prompt = q.Mode == LearningMode.Blank ? q.Blanks ?? "" : q.Japanese;
        LetterCount = q.Mode == LearningMode.Blank ? q.Word.Length : null;
        IsRetry = q.IsRetry;
        AnswerCommand = Command(() => Shell.SubmitAnswer(this, Input), () => !IsInputBlank);
        QuitCommand = Command(() =>
        {
            if (Shell.Prompt.Confirm(QuitConfirmMessage))
            {
                Invoke(flow => flow.Quit());
            }
        });
    }

    public int Number { get; }

    public int Total { get; }

    /// <summary>進捗バーの割合(0〜1)。答え終えた問題の数 / 合計。</summary>
    public double Progress => Total == 0 ? 0 : (double)(Number - 1) / Total;

    public LearningMode Mode { get; }

    public string ModeName { get; }

    /// <summary>日本語モードは意味、穴あきモードは穴あき表示。</summary>
    public string Prompt { get; }

    public bool IsBlankMode => Mode == LearningMode.Blank;

    /// <summary>穴あきモードの文字数(仕様 3.3)。</summary>
    public int? LetterCount { get; }

    public bool IsRetry { get; }

    public string? RetryNote => IsRetry ? "間違えた単語の再練習です(合計が増えました)" : null;

    public string Input
    {
        get => _input;
        set
        {
            if (SetProperty(ref _input, value ?? ""))
            {
                OnPropertyChanged(nameof(IsInputBlank));
                AnswerCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsInputBlank => SpellingJudge.IsBlank(Input);

    /// <summary>回答欄の文字数の上限(誤答入力の記録の上限と同じ)。</summary>
    public static int MaxInputLength => InputRules.MaxStoredInputLength;

    public RelayCommand AnswerCommand { get; }

    public RelayCommand QuitCommand { get; }

    /// <summary>テスト用(正解を打つため)。画面には出さない。</summary>
    internal string WordForTests => _word;

    public override string ToString() => $"QuestionViewModel {{ Number = {Number}, Total = {Total}, Input = (伏せる) }}";
}
