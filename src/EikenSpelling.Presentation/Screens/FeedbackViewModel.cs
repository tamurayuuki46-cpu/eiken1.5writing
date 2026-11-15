using EikenSpelling.Flow;
using EikenSpelling.Presentation.Mvvm;
using EikenSpelling.Presentation.Text;

namespace EikenSpelling.Presentation.Screens;

/// <summary>
/// 判定(R11・R13)。n / 合計と入力は ShellViewModel.LastAnswer から(再保存で作り直されても同じ値)。
/// 保存できていない間は「次へ」を無効にし、「もう一度保存」を出す。
/// </summary>
public sealed class FeedbackViewModel : ScreenViewModel
{
    public FeedbackViewModel(ShellViewModel shell, FeedbackScreen screen, LastAnswer last)
        : base(shell)
    {
        IsCorrect = screen.IsCorrect;
        IsRevealed = screen.Revealed;
        CorrectWord = screen.CorrectWord;
        Diff = new DiffDisplay(screen.CorrectWord, last.Input, screen.Diff);
        Changes = ScreenText.Changes(screen.Change);
        Number = last.Number;
        Total = last.Total;
        ProgressText = last.ProgressText;
        Saved = screen.Saved;
        SaveFailure = screen.SaveFailure;
        IsLast = screen.Completed;
        NextCommand = Command(() => Invoke(flow => flow.Next()), () => Saved);
        RetrySaveCommand = Command(() => Invoke(flow => flow.RetrySave()), () => !Saved);
    }

    public bool IsCorrect { get; }

    /// <summary>後回しで答えを見た不正解。今回の入力も差分も無い。</summary>
    public bool IsRevealed { get; }

    public string ResultText => IsRevealed ? ScreenText.RevealedResult : IsCorrect ? "正解" : "不正解";

    /// <summary>「あなたの入力」を出すか(答えを見たときは今回の入力が無いので出さない)。</summary>
    public bool HasInput => !IsCorrect && !IsRevealed;

    public string CorrectWord { get; }

    public DiffDisplay Diff { get; }

    public IReadOnlyList<string> Changes { get; }

    public int Number { get; }

    public int Total { get; }

    /// <summary>直前の出題の段階別の進み具合の1行。</summary>
    public string ProgressText { get; }

    public bool Saved { get; }

    /// <summary>保存失敗の文言(Flow)。</summary>
    public string? SaveFailure { get; }

    /// <summary>最後の問題(次は結果)。</summary>
    public bool IsLast { get; }

    public string NextLabel => IsLast ? "結果を見る" : "次へ";

    public RelayCommand NextCommand { get; }

    public RelayCommand RetrySaveCommand { get; }

    public override string ToString() => $"FeedbackViewModel {{ IsCorrect = {IsCorrect}, Saved = {Saved}, Input = (伏せる) }}";
}
