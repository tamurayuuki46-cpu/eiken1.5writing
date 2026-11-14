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
        CorrectWord = screen.CorrectWord;
        Diff = new DiffDisplay(screen.CorrectWord, last.Input, screen.Diff);
        Changes = ScreenText.Changes(screen.Change);
        Number = last.Number;
        Total = last.Total;
        Saved = screen.Saved;
        SaveFailure = screen.SaveFailure;
        IsLast = screen.Completed;
        NextCommand = Command(() => Invoke(flow => flow.Next()), () => Saved);
        RetrySaveCommand = Command(() => Invoke(flow => flow.RetrySave()), () => !Saved);
    }

    public bool IsCorrect { get; }

    public string ResultText => IsCorrect ? "正解" : "不正解";

    public string CorrectWord { get; }

    public DiffDisplay Diff { get; }

    public IReadOnlyList<string> Changes { get; }

    public int Number { get; }

    public int Total { get; }

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
