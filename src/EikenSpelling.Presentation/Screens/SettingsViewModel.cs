using EikenSpelling.Core.Models;
using EikenSpelling.Flow;
using EikenSpelling.Presentation.Mvvm;
using EikenSpelling.Presentation.Text;

namespace EikenSpelling.Presentation.Screens;

/// <summary>
/// 設定画面(UI v0.3 S-07)。基本設定(問題数・出題方法・苦手のみ・フラッシュ秒数)と1日の目標を編集し、保存で確定する。
/// 「初期値に戻す」は画面の値だけを戻し(保存で確定)、「閉じる」は保存せずにホームへ。保存できるか・検査・失敗の文言は Flow が決める。
/// </summary>
public sealed class SettingsViewModel : ScreenViewModel
{
    private readonly PracticeChoice _values;
    private int _selectedQuestionCount;
    private ModeOption _selectedMode;
    private bool _weakOnly;
    private double _selectedFlashSeconds;
    private int _selectedDailyGoal;

    public SettingsViewModel(ShellViewModel shell, SettingsScreen screen)
        : base(shell)
    {
        _values = screen.Values;
        CanSave = screen.CanSave;
        CannotSaveReason = screen.CannotSaveReason;
        Note = screen.Note;
        Message = screen.Message;
        Issues = [.. screen.Issues.Select(i => i.Message)];

        // 画面の値が選択肢に無くても(手で書いた settings.json など)その値を出す
        QuestionCountOptions = [.. screen.QuestionCountChoices.Append(_values.QuestionCount).Distinct().Order()];
        FlashSecondsOptions = [.. screen.FlashSecondsChoices.Append(_values.FlashSeconds).Distinct().Order()];
        DailyGoalOptions = [.. screen.DailyGoalChoices.Append(screen.DailyGoal).Distinct().Order()];
        ModeOptions = HomeViewModel.BuildModeOptions(shell.AvailableModes, _values);
        _selectedQuestionCount = _values.QuestionCount;
        _selectedMode = _values.AutoMix ? ModeOptions[0] : ModeOptions.First(o => !o.AutoMix && o.Mode == _values.ManualMode);
        _weakOnly = _values.WeakWordsOnly;
        _selectedFlashSeconds = _values.FlashSeconds;
        _selectedDailyGoal = screen.DailyGoal;

        SaveCommand = Command(() => Invoke(flow => flow.SaveSettings(Choice, SelectedDailyGoal)), () => CanSave && SelectedMode.Available);
        ResetCommand = Command(() => Invoke(flow => flow.ResetSettings()));
        CloseCommand = Command(() => Invoke(flow => flow.CloseSettings()));
    }

    public bool CanSave { get; }

    /// <summary>保存できない理由(保存ボタンは押せない)。</summary>
    public string? CannotSaveReason { get; }

    /// <summary>保存の前に知らせる注意(壊れた設定ファイルを退避して保存するなど)。</summary>
    public string? Note { get; }

    /// <summary>保存の失敗・検査の失敗の文言(警告の帯)。</summary>
    public string? Message { get; }

    /// <summary>検査で見つかった項目の文言。</summary>
    public IReadOnlyList<string> Issues { get; }

    public IReadOnlyList<int> QuestionCountOptions { get; }

    public IReadOnlyList<ModeOption> ModeOptions { get; }

    public IReadOnlyList<double> FlashSecondsOptions { get; }

    public IReadOnlyList<int> DailyGoalOptions { get; }

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
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool WeakOnly
    {
        get => _weakOnly;
        set => SetProperty(ref _weakOnly, value);
    }

    public double SelectedFlashSeconds
    {
        get => _selectedFlashSeconds;
        set => SetProperty(ref _selectedFlashSeconds, value);
    }

    /// <summary>1日の目標(1回の練習の出題数とは別)。</summary>
    public int SelectedDailyGoal
    {
        get => _selectedDailyGoal;
        set => SetProperty(ref _selectedDailyGoal, value);
    }

    /// <summary>端末で使えないモードを選んでいるときの理由(保存は押せない)。</summary>
    public string? SelectedModeUnavailableNote =>
        SelectedMode.Available ? null
        : SelectedMode.Mode == LearningMode.Audio ? ScreenText.AudioUnavailable
        : ScreenText.EmptyReason(Core.Application.SelectionEmptyReason.ManualModeUnavailable);

    public RelayCommand SaveCommand { get; }

    public RelayCommand ResetCommand { get; }

    public RelayCommand CloseCommand { get; }

    /// <summary>画面の値。自動ミックスのときの手動モードは画面を開いたときの値のまま。</summary>
    public PracticeChoice Choice => new(
        SelectedMode.AutoMix,
        SelectedMode.AutoMix ? _values.ManualMode : SelectedMode.Mode,
        SelectedQuestionCount,
        WeakOnly,
        SelectedFlashSeconds);
}
