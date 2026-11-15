using EikenSpelling.Core.Application;
using EikenSpelling.Flow;
using EikenSpelling.Presentation.Mvvm;
using EikenSpelling.Presentation.Text;

namespace EikenSpelling.Presentation.Screens;

/// <summary>苦手一覧で最初にフォーカスを置く操作(並べ替え・絞り込みの後は押したボタンに戻す)。</summary>
public enum WeakListFocus
{
    SortWeakFirst,
    SortRecent,
    WeakOnly,
}

/// <summary>
/// 苦手一覧(S-05・UC-05/06/23)。結果またはホームから開き、「戻る」は開いた画面に戻す(Flow が行き先を持つ)。
/// 「苦手語をまとめて復習」は Flow の StartWeakPractice(苦手の判定・適格性・優先順位は既存)。
/// 復習の対象が無ければ(Flow の HasReviewTargets)ボタンを押せなくし、理由を出す(UC-30)。
/// 並べ替え・絞り込みで作り直しても、開いていた行と押したボタンへのフォーカスを引き継ぐ。
/// </summary>
public sealed class WeakListViewModel : ScreenViewModel
{
    private readonly PracticeChoice _defaults;
    private WeakListFocus _pressed;

    public WeakListViewModel(ShellViewModel shell, WeakListScreen screen, WeakListViewModel? previous = null)
        : base(shell)
    {
        _defaults = screen.Defaults;
        Origin = screen.Origin;
        Sort = screen.Sort;
        WeakOnly = screen.WeakOnly;
        HasReviewTargets = screen.HasReviewTargets;
        InitialFocus = previous?._pressed ?? WeakListFocus.SortWeakFirst;
        var expanded = previous?.Rows.Where(r => r.IsExpanded).Select(r => r.WordId).ToHashSet(StringComparer.Ordinal) ?? [];
        Rows = [.. screen.Rows.Select(r => new WeakRowViewModel(r, expanded.Contains(r.WordId)))];
        foreach (var row in Rows)
        {
            var captured = row;
            captured.ToggleCommand = Command(captured.Toggle);
        }

        WeakCount = Rows.Count(r => r.IsWeak);
        MasteredCount = Rows.Count - WeakCount;
        InfoMessage = ScreenText.WeakListEmptyReason(screen.EmptyReason);
        WarningMessage = screen.Message;
        BackLabel = Origin == WeakListOrigin.Result ? ScreenText.BackToResult : ScreenText.BackToHome;
        ReviewCommand = Command(() => Invoke(flow => flow.StartWeakPractice(_defaults)), () => HasReviewTargets);
        BackCommand = Command(() => Invoke(flow => flow.CloseWeakList()));
        SortWeakFirstCommand = Command(() => Change(WeakListFocus.SortWeakFirst, WeakListSort.WeakFirst, WeakOnly));
        SortRecentCommand = Command(() => Change(WeakListFocus.SortRecent, WeakListSort.RecentMistake, WeakOnly));
        ToggleWeakOnlyCommand = Command(() => Change(WeakListFocus.WeakOnly, Sort, !WeakOnly));
    }

    public WeakListOrigin Origin { get; }

    public WeakListSort Sort { get; }

    public bool WeakOnly { get; }

    /// <summary>一括復習の対象(苦手か、期限の来た克服済み)があるか(Flow の値)。</summary>
    public bool HasReviewTargets { get; }

    /// <summary>対象が無いときの理由(ボタンの下に常に出す)。</summary>
    public string? ReviewUnavailableText => HasReviewTargets ? null : ScreenText.NoReviewTargets;

    /// <summary>最初にフォーカスを置く操作(View が使う)。開いた直後は「苦手度の順」、並べ替えの後は押したボタン。</summary>
    public WeakListFocus InitialFocus { get; }

    public IReadOnlyList<WeakRowViewModel> Rows { get; }

    public bool IsEmpty => Rows.Count == 0;

    public string EmptyText => ScreenText.NoWeakWordsInList;

    public int WeakCount { get; }

    public int MasteredCount { get; }

    /// <summary>直前の一括復習が0語だった理由(情報の帯)。</summary>
    public string? InfoMessage { get; }

    /// <summary>直前の一括復習が保存に失敗した(警告の帯)。</summary>
    public string? WarningMessage { get; }

    public string BackLabel { get; }

    public string SortWeakFirstLabel => ScreenText.SortLabel(WeakListSort.WeakFirst, Sort == WeakListSort.WeakFirst);

    public string SortRecentLabel => ScreenText.SortLabel(WeakListSort.RecentMistake, Sort == WeakListSort.RecentMistake);

    public string WeakOnlyLabel => ScreenText.WeakOnlyToggle(WeakOnly);

    /// <summary>「苦手語をまとめて復習」。</summary>
    public RelayCommand ReviewCommand { get; }

    public RelayCommand BackCommand { get; }

    public RelayCommand SortWeakFirstCommand { get; }

    public RelayCommand SortRecentCommand { get; }

    public RelayCommand ToggleWeakOnlyCommand { get; }

    private void Change(WeakListFocus pressed, WeakListSort sort, bool weakOnly)
    {
        _pressed = pressed;
        Invoke(flow => flow.ChangeWeakList(sort, weakOnly));
    }
}
