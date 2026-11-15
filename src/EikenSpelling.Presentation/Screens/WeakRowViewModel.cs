using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;
using EikenSpelling.Presentation.Mvvm;
using EikenSpelling.Presentation.Text;

namespace EikenSpelling.Presentation.Screens;

/// <summary>
/// 苦手一覧の1行(UI v0.3 S-05)。展開すると 正しい綴り・直近に本人が入力した誤綴り・モード別の状態・克服/再確認の情報 が見える。
/// 展開は画面の状態だけで、Flow も保存も呼ばない(閲覧は何も書かない)。入力は誤答入力なので ToString に出さない。
/// </summary>
public sealed class WeakRowViewModel : ObservableObject
{
    private bool _isExpanded;

    public WeakRowViewModel(WeakWordRow row, bool expanded = false)
    {
        WordId = row.WordId;
        Word = row.Word;
        IsWeak = row.State == LearningState.Weak;
        StateText = ScreenText.WeakRowState(row.State);
        InWordSet = row.InWordSet;
        SummaryText = ScreenText.NormalMistakes(row.NormalMistakes);

        // 実入力の履歴が無ければ「記録なし」(答えを見るだけで間違えた語など)。入力と最後の誤答の日時は別に出す
        TypedInput = row.LastWrongInput ?? ScreenText.NoTypedInput;
        HasTypedInput = row.LastWrongInput is not null && row.LastWrongInputMode is not null;
        TypedInputMeta = HasTypedInput ? ScreenText.TypedInputMeta(row.LastWrongInputMode!.Value, row.LastWrongInputAt) : "";
        LastMistakeText = ScreenText.DateTimeOrNone(row.LastWrongAt);
        ModeLines = [.. row.Modes.Select(ScreenText.ModeLine)];
        StatusText = ScreenText.WeakStatus(row);
        _isExpanded = expanded;
    }

    public string WordId { get; }

    public string Word { get; }

    public bool IsWeak { get; }

    public string StateText { get; }

    /// <summary>単語データに無い単語(ID で出している)。</summary>
    public bool InWordSet { get; }

    public string SummaryText { get; }

    /// <summary>直近に本人が入力した誤った綴り。履歴が無ければ「記録なし」。画面にだけ出す。</summary>
    public string TypedInput { get; }

    public bool HasTypedInput { get; }

    /// <summary>実入力のモードと日時(「(日本語・11月14日 20:00)」)。入力が無ければ空。</summary>
    public string TypedInputMeta { get; }

    /// <summary>最後の誤答の日時(答えを見る不正解を含む)。</summary>
    public string LastMistakeText { get; }

    public IReadOnlyList<string> ModeLines { get; }

    public string StatusText { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        private set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(ToggleLabel));
            }
        }
    }

    /// <summary>「詳細」/「閉じる」(開いているかを文字でも示す)。</summary>
    public string ToggleLabel => IsExpanded ? ScreenText.CloseDetail : ScreenText.OpenDetail;

    /// <summary>展開・閉じる(WeakListViewModel が画面のゲートを通して作る)。</summary>
    public RelayCommand ToggleCommand { get; internal set; } = null!;

    internal void Toggle() => IsExpanded = !IsExpanded;

    public override string ToString() => $"WeakRowViewModel {{ State = {StateText}, IsExpanded = {IsExpanded}, Input = (伏せる) }}";
}
