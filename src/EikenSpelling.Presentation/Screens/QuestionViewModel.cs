using EikenSpelling.Core.Application;
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

    public QuestionViewModel(ShellViewModel shell, QuestionScreen screen, string? keptInput = null)
        : base(shell)
    {
        _input = keptInput ?? "";
        var q = screen.Question;
        _word = q.Word;
        Number = screen.Number;
        Total = screen.Total;
        Answered = screen.Answered;
        QueueLength = screen.QueueLength;
        ProgressText = ScreenText.ProgressText(screen.Phase, screen.Number, screen.Total);
        Mode = q.Mode;
        ModeName = ScreenText.ModeName(q.Mode);
        Prompt = q.Mode == LearningMode.Blank ? q.Blanks ?? "" : q.Japanese;
        LetterCount = q.Mode == LearningMode.Blank ? q.Word.Length : null;
        IsRetry = q.IsRetry;
        Phase = screen.Phase;
        PhaseName = ScreenText.PhaseName(screen.Phase);
        CanSkip = screen.CanSkip;
        SkipSaveFailure = screen.SkipSaveFailure;
        AnswerCommand = Command(() => Shell.SubmitAnswer(this, Input), () => !IsInputBlank && !IsSkipSavePending);

        // 後回しにできるかは Flow(Core)が決めた CanSkip だけで決める(画面で判断しない)
        SkipCommand = Command(() => Invoke(flow => flow.Skip()), () => CanSkip && !IsSkipSavePending);
        RetrySaveCommand = Command(() => Invoke(flow => flow.RetrySave()), () => IsSkipSavePending);
        QuitCommand = Command(() =>
        {
            if (Shell.Prompt.Confirm(QuitConfirmMessage))
            {
                Invoke(flow => flow.Quit());
            }
        }, () => !IsSkipSavePending);
    }

    public int Number { get; }

    public int Total { get; }

    /// <summary>練習全体の採点済みの数と列の長さ(段階が変わってもバーが巻き戻らないように)。</summary>
    public int Answered { get; }

    public int QueueLength { get; }

    /// <summary>進捗バーの割合(0〜1)。練習全体の採点済みの数 / 列の長さ。</summary>
    public double Progress => QueueLength == 0 ? 0 : (double)Answered / QueueLength;

    /// <summary>段階別の進み具合の1行(「通常:全5問中の2問目」など)。</summary>
    public string ProgressText { get; }

    public LearningMode Mode { get; }

    public string ModeName { get; }

    /// <summary>日本語モードは意味、穴あきモードは穴あき表示。</summary>
    public string Prompt { get; }

    public bool IsBlankMode => Mode == LearningMode.Blank;

    /// <summary>穴あきモードの文字数(仕様 3.3)。</summary>
    public int? LetterCount { get; }

    public bool IsRetry { get; }

    public QuestionPhase Phase { get; }

    /// <summary>今の段階の名前(通常 / 後回し / 再練習)。</summary>
    public string PhaseName { get; }

    public bool IsDeferred => Phase == QuestionPhase.Deferred;

    /// <summary>「後回し」ボタンを出すか(通常の段階。保存待ちの間は出したまま押せなくする)。</summary>
    public bool IsNormalPhase => Phase == QuestionPhase.Normal;

    /// <summary>キー操作の案内。保存待ちの間は Enter・Esc が効かないので案内を替える。</summary>
    public string KeyHint => IsSkipSavePending
        ? "「もう一度保存」を押してください(回答・やめるは保存の後に押せます)"
        : "Enter で回答 ・ Esc でやめる";

    /// <summary>今後回しにできるか(Flow の値。保存待ちの間は偽)。</summary>
    public bool CanSkip { get; }

    /// <summary>後回しの保存に失敗した文言(Flow)。ある間は「もう一度保存」だけ押せる。</summary>
    public string? SkipSaveFailure { get; }

    public bool IsSkipSavePending => SkipSaveFailure is not null;

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

    public RelayCommand SkipCommand { get; }

    public RelayCommand RetrySaveCommand { get; }

    /// <summary>テスト用(正解を打つため)。画面には出さない。</summary>
    internal string WordForTests => _word;

    public override string ToString() => $"QuestionViewModel {{ Number = {Number}, Total = {Total}, Input = (伏せる) }}";
}
