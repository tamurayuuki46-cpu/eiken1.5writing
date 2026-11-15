using EikenSpelling.Presentation.Mvvm;
using EikenSpelling.Presentation.Text;

namespace EikenSpelling.Presentation.Screens;

/// <summary>
/// セット限定の開始の確認(UC-09)。「希望20問/出題可能8問」と「8問で開始」「条件に戻る」。0問なら理由と「条件に戻る」だけ。
/// 出題の語は画面に出さない(音声・フラッシュでは答えになる)。開始の保存に失敗したら、同じ計画で保存し直せる。
/// </summary>
public sealed class SetStartPreviewViewModel : ScreenViewModel
{
    public SetStartPreviewViewModel(ShellViewModel shell, Flow.SetStartPreviewScreen screen)
        : base(shell)
    {
        Desired = screen.Desired;
        Available = screen.Available;
        CanStart = screen.CanStart;
        IsShort = screen.IsShort;
        SetText = ScreenText.SetNumberText(screen.Number, screen.Total);
        Headline = !CanStart ? ScreenText.SetNothingHeadline
            : IsShort ? ScreenText.SetShortHeadline(Available, Desired)
            : ScreenText.SetFullHeadline(Available);
        ReasonText = CanStart ? null : ScreenText.EmptyReason(screen.EmptyReason);
        Hint = CanStart ? null : ScreenText.SetNothingHint;
        SaveFailure = screen.SaveFailure;
        StaleNotice = screen.Stale ? ScreenText.SetPlanChanged : null;
        StartLabel = SaveFailure is not null ? ScreenText.SetRetryStartLabel : ScreenText.SetStartLabel(Available);
        ConfirmCommand = Command(() => Invoke(flow => flow.ConfirmSetStart()), () => CanStart);
        CancelCommand = Command(() => Invoke(flow => flow.CancelSetStart()));
    }

    public int Desired { get; }

    public int Available { get; }

    public bool CanStart { get; }

    public bool IsShort { get; }

    /// <summary>「セット 1 / 99」。</summary>
    public string SetText { get; }

    public string Headline { get; }

    /// <summary>0問のときの理由。</summary>
    public string? ReasonText { get; }

    public string? Hint { get; }

    /// <summary>開始の保存に失敗したときの文言(原因はログへ)。</summary>
    public string? SaveFailure { get; }

    /// <summary>確認の前提が変わって計画を作り直したときの案内。</summary>
    public string? StaleNotice { get; }

    public string StartLabel { get; }

    public RelayCommand ConfirmCommand { get; }

    public RelayCommand CancelCommand { get; }
}
