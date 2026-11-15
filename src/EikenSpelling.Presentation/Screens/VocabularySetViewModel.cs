using EikenSpelling.Core.Application;
using EikenSpelling.Flow;
using EikenSpelling.Presentation.Mvvm;
using EikenSpelling.Presentation.Text;

namespace EikenSpelling.Presentation.Screens;

/// <summary>語彙セットの一覧の1行。成績は WordId 別の記録の写し(読み取りだけ)。</summary>
public sealed class VocabularyRowViewModel(VocabularyWordRow row)
{
    public string WordId { get; } = row.WordId;

    public string Word { get; } = row.Word;

    public string Japanese { get; } = row.Japanese;

    public WordStudyState State { get; } = row.State;

    public string StateText { get; } = ScreenText.StudyState(row.State);

    public bool IsWeak { get; } = row.State == WordStudyState.Weak;
}

/// <summary>語彙セットの画面で最初にフォーカスを置く操作(ページ送り・単位切替の後は押したボタンに戻す)。</summary>
public enum VocabularySetFocus
{
    Unit,
    Previous,
    Next,
    Start,
}

/// <summary>
/// 語彙セット(S-06・UC-07〜10)。50語/100語の切り替え・前後移動・収録語と学習状態・選択中のセットで学習を始める。
/// 開く・送る・切り替える・戻るは何も書かない(Flow が保証)。開始条件(問題数・出題方法・苦手のみ)は今回だけ。
/// ページ送り・単位切替で作り直しても、選んでいた条件と押したボタンのフォーカスを引き継ぐ。
/// </summary>
public sealed class VocabularySetViewModel : ScreenViewModel
{
    private readonly PracticeChoice _initial;
    private int _selectedQuestionCount;
    private ModeOption _selectedMode;
    private bool _weakOnly;
    private VocabularySetFocus _pressed;

    public VocabularySetViewModel(
        ShellViewModel shell, VocabularySetScreen screen, VocabularySetViewModel? previous = null, bool returnedFromPreview = false)
        : base(shell)
    {
        _initial = previous?.Choice ?? screen.Choice;
        UnitSize = screen.UnitSize;
        Number = screen.Number;
        Total = screen.Total;
        TotalWords = screen.TotalWords;
        CanStart = screen.CanStart;
        PageText = ScreenText.SetPageText(UnitSize, Number, Total, TotalWords);
        IsUnit50Selected = UnitSize == 50;
        Unit50Label = ScreenText.SetUnitLabel(50, IsUnit50Selected);
        Unit100Label = ScreenText.SetUnitLabel(100, !IsUnit50Selected);
        Rows = [.. screen.Rows.Select(r => new VocabularyRowViewModel(r))];
        // 確認の画面から「条件に戻る」で戻ったときは「このセットで始める」へ、ページ送り・単位切替の後は押したボタンへ
        InitialFocus = previous?._pressed ?? (returnedFromPreview ? VocabularySetFocus.Start : VocabularySetFocus.Unit);

        QuestionCountOptions = [.. screen.QuestionCountChoices.Append(_initial.QuestionCount).Distinct().Order()];
        ModeOptions = HomeViewModel.BuildModeOptions(shell.AvailableModes, _initial);
        _selectedQuestionCount = _initial.QuestionCount;
        _selectedMode = _initial.AutoMix ? ModeOptions[0] : ModeOptions.First(o => !o.AutoMix && o.Mode == _initial.ManualMode);
        _weakOnly = _initial.WeakWordsOnly;
        AudioUnavailableNote = shell.AvailableModes.Contains(Core.Models.LearningMode.Audio) ? null : ScreenText.AudioUnavailable;

        PreviousCommand = Command(() => Go(VocabularySetFocus.Previous, flow => flow.PreviousSet()), () => screen.CanPrevious);
        NextCommand = Command(() => Go(VocabularySetFocus.Next, flow => flow.NextSet()), () => screen.CanNext);
        Unit50Command = Command(() => Go(VocabularySetFocus.Unit, flow => flow.ChangeSetUnit(50)));
        Unit100Command = Command(() => Go(VocabularySetFocus.Unit, flow => flow.ChangeSetUnit(100)));
        BackCommand = Command(() => Invoke(flow => flow.CloseVocabularySets()));
        StartCommand = Command(() => Invoke(flow => flow.PreviewSetPractice(Choice)), () => CanStart && SelectedMode.Available);
    }

    public int UnitSize { get; }

    public int Number { get; }

    public int Total { get; }

    public int TotalWords { get; }

    public bool CanStart { get; }

    public string PageText { get; }

    public bool IsUnit50Selected { get; }

    public string Unit50Label { get; }

    public string Unit100Label { get; }

    public IReadOnlyList<VocabularyRowViewModel> Rows { get; }

    public VocabularySetFocus InitialFocus { get; }

    public IReadOnlyList<int> QuestionCountOptions { get; }

    public IReadOnlyList<ModeOption> ModeOptions { get; }

    public string? AudioUnavailableNote { get; }

    public int SelectedQuestionCount
    {
        get => _selectedQuestionCount;
        set => SetProperty(ref _selectedQuestionCount, value);
    }

    public ModeOption SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (SetProperty(ref _selectedMode, value))
            {
                OnPropertyChanged(nameof(SelectedModeUnavailableNote));
                StartCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool WeakOnly
    {
        get => _weakOnly;
        set => SetProperty(ref _weakOnly, value);
    }

    /// <summary>端末で使えないモードを選んでいるときの理由(開始は押せない)。</summary>
    public string? SelectedModeUnavailableNote =>
        SelectedMode.Available ? null
        : SelectedMode.Mode == Core.Models.LearningMode.Audio ? ScreenText.AudioUnavailable
        : ScreenText.EmptyReason(SelectionEmptyReason.ManualModeUnavailable);

    public RelayCommand PreviousCommand { get; }

    public RelayCommand NextCommand { get; }

    public RelayCommand Unit50Command { get; }

    public RelayCommand Unit100Command { get; }

    public RelayCommand BackCommand { get; }

    /// <summary>このセットで始める(足りなければ確認の画面へ。Flow が決める)。</summary>
    public RelayCommand StartCommand { get; }

    /// <summary>画面の値をそのまま渡す。自動ミックスのときの手動モード・フラッシュ秒数は、元の値のまま。</summary>
    public PracticeChoice Choice => new(
        SelectedMode.AutoMix,
        SelectedMode.AutoMix ? _initial.ManualMode : SelectedMode.Mode,
        SelectedQuestionCount,
        WeakOnly,
        _initial.FlashSeconds);

    private void Go(VocabularySetFocus pressed, Action<PracticeFlow> operation)
    {
        _pressed = pressed;
        Invoke(operation);
    }
}
