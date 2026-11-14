using EikenSpelling.Core.Application;
using EikenSpelling.Core.Domain;
using EikenSpelling.Flow;
using EikenSpelling.Presentation.Mvvm;
using EikenSpelling.Presentation.Text;

namespace EikenSpelling.Presentation.Screens;

/// <summary>再練習の結果の1行。</summary>
public sealed record RetryRow(string Word, bool FinallyCorrect)
{
    public string ResultText => FinallyCorrect ? "正解" : "不正解";
}

/// <summary>苦手状態の変化の1グループ。Words が空なら「なし」。</summary>
public sealed record ChangeGroup(string Title, IReadOnlyList<string> Words)
{
    public string WordsText => Words.Count == 0 ? "なし" : string.Join("、", Words);
}

/// <summary>結果(R16)。集計は Core の SessionSummary。</summary>
public sealed class ResultViewModel : ScreenViewModel
{
    public ResultViewModel(ShellViewModel shell, ResultScreen screen)
        : base(shell)
    {
        var s = screen.Summary;
        NormalCorrect = s.NormalCorrect;
        NormalCount = s.NormalCount;
        MistakenWords = [.. s.MistakenWords.Select(w => w.Word)];
        Retries = [.. s.Retries.Select(r => new RetryRow(r.Word, r.FinallyCorrect))];
        ChangeGroups = [.. Enum.GetValues<StateChangeKind>().Select(kind => new ChangeGroup(
            ScreenText.StateChange(kind),
            [.. s.StateChanges.Where(c => c.Kind == kind).Select(c => c.Word)]))];
        GoHomeCommand = Command(() => Invoke(flow => flow.GoHome()));
    }

    public int NormalCorrect { get; }

    public int NormalCount { get; }

    public IReadOnlyList<string> MistakenWords { get; }

    public IReadOnlyList<RetryRow> Retries { get; }

    public IReadOnlyList<ChangeGroup> ChangeGroups { get; }

    public RelayCommand GoHomeCommand { get; }
}

/// <summary>再開の選択(R15)。</summary>
public sealed class ResumePromptViewModel : ScreenViewModel
{
    public ResumePromptViewModel(ShellViewModel shell, ResumePromptScreen screen)
        : base(shell)
    {
        StartedAtText = JstClock.TryToJst(screen.Session.StartedAt)?.ToString("M月d日 HH:mm") ?? "";
        Answered = screen.Session.Answered;
        Total = screen.Session.Total;
        CanResume = screen.CanResume;
        CannotResumeReason = screen.CannotResumeReason;
        Message = screen.Message;
        ResumeCommand = Command(() => Invoke(flow => flow.Resume()), () => CanResume);
        DiscardCommand = Command(() => Invoke(flow => flow.DiscardAndGoHome()));
    }

    public string StartedAtText { get; }

    public int Answered { get; }

    public int Total { get; }

    public bool CanResume { get; }

    public string? CannotResumeReason { get; }

    public string? Message { get; }

    public RelayCommand ResumeCommand { get; }

    public RelayCommand DiscardCommand { get; }
}

/// <summary>両方破損(R17)。「新しい記録で始める」は確認してから。</summary>
public sealed class BothCorruptViewModel : ScreenViewModel
{
    public const string StartFreshConfirmMessage = "新しい記録で始めますか?\nこれまでの学習状況は引き継がれず、空の記録から始まります。壊れたファイルは残ります。";

    public BothCorruptViewModel(ShellViewModel shell, BothCorruptScreen screen)
        : base(shell)
    {
        Message = screen.Message;
        StartFreshCommand = Command(() =>
        {
            if (Shell.Prompt.Confirm(StartFreshConfirmMessage))
            {
                Invoke(flow => flow.StartFresh());
            }
        });
        ExitCommand = Command(() => Shell.RequestExit(this));
    }

    public string? Message { get; }

    public RelayCommand StartFreshCommand { get; }

    public RelayCommand ExitCommand { get; }
}

/// <summary>終了のみ(R18)。読めないとき(Unavailable)だけ再読込できる。</summary>
public sealed class FatalViewModel : ScreenViewModel
{
    public FatalViewModel(ShellViewModel shell, FatalScreen screen)
        : base(shell)
    {
        Reason = screen.Reason;
        CanReload = screen.CanReload;
        ReloadCommand = Command(() => Invoke(flow => flow.Reload()), () => CanReload);
        ExitCommand = Command(() => Shell.RequestExit(this));
    }

    public string Reason { get; }

    public bool CanReload { get; }

    public RelayCommand ReloadCommand { get; }

    public RelayCommand ExitCommand { get; }
}
