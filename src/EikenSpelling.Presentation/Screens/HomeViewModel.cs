using EikenSpelling.Core.Models;
using EikenSpelling.Flow;
using EikenSpelling.Presentation.Mvvm;
using EikenSpelling.Presentation.Text;

namespace EikenSpelling.Presentation.Screens;

/// <summary>出題方法の選択肢(自動ミックス、または手動のモード)。Available が偽なら端末で使えない(理由を出す)。</summary>
public sealed record ModeOption(string Label, bool AutoMix, LearningMode Mode, bool Available = true);

/// <summary>
/// ホーム(R6・R7、UI v0.3 S-02)。学習状況5項目と、同じ大きさの「おすすめ学習」「自由選択」。
/// おすすめの条件(保存済みの問題数・自動ミックス ON・苦手のみ OFF)は Flow の StartRecommended が決める。
/// 自由選択はホーム内に展開し、今回だけの条件で始める(基本設定に書き戻さない)。範囲の検査・0語の判断は Flow と Core。
/// </summary>
public sealed class HomeViewModel : ScreenViewModel
{
    private readonly PracticeChoice _defaults;
    private readonly IReadOnlySet<LearningMode> _available;
    private int _selectedQuestionCount;
    private ModeOption _selectedMode;
    private bool _weakOnly;
    private double _selectedFlashSeconds;
    private bool _isFreeChoiceOpen;

    public HomeViewModel(ShellViewModel shell, HomeScreen screen, PracticeChoice? previous = null, bool freeChoiceOpen = false)
        : base(shell)
    {
        _defaults = screen.Defaults;
        _available = shell.AvailableModes;
        var initial = previous ?? screen.Defaults;
        Unseen = screen.Status.Unseen;
        Normal = screen.Status.Normal;
        Weak = screen.Status.Weak;
        Mastered = screen.Status.Mastered;
        ReviewDue = screen.Status.ReviewDue;
        InfoMessage = ScreenText.EmptyReason(screen.EmptyReason);
        WarningMessage = screen.Message;
        SavedMessage = screen.Info;
        TodayText = ScreenText.TodayText(screen.Today);
        TodayNote = ScreenText.TodayNote(screen.Today);
        TodayAchieved = screen.Today.Achieved;
        AudioUnavailableNote = _available.Contains(LearningMode.Audio) ? null : ScreenText.AudioUnavailable;

        QuestionCountOptions = [.. screen.QuestionCountChoices.Append(initial.QuestionCount).Distinct().Order()];
        FlashSecondsOptions = [.. screen.FlashSecondsChoices.Append(initial.FlashSeconds).Distinct().Order()];
        ModeOptions = BuildModeOptions(_available, initial);
        _selectedQuestionCount = initial.QuestionCount;
        _selectedMode = initial.AutoMix ? ModeOptions[0] : ModeOptions.First(o => !o.AutoMix && o.Mode == initial.ManualMode);
        _weakOnly = initial.WeakWordsOnly;
        _selectedFlashSeconds = initial.FlashSeconds;

        // 自由選択で始めて0語・保存失敗で戻ったときは、開いたまま理由を見せる(おすすめ学習からなら閉じたまま)
        _isFreeChoiceOpen = freeChoiceOpen;

        RecommendedCommand = Command(() => Invoke(flow => flow.StartRecommended()));
        ToggleFreeChoiceCommand = Command(() => IsFreeChoiceOpen = !IsFreeChoiceOpen);
        OpenWeakListCommand = Command(() => Invoke(flow => flow.OpenWeakList()));
        OpenVocabularySetsCommand = Command(() => Invoke(flow => flow.OpenVocabularySets()));
        OpenSettingsCommand = Command(() => Invoke(flow => flow.OpenSettings()));
        StartCommand = Command(() => Invoke(flow => flow.StartPractice(Choice)), () => SelectedMode.Available);
    }

    public int Unseen { get; }

    public int Normal { get; }

    public int Weak { get; }

    public int Mastered { get; }

    public int ReviewDue { get; }

    /// <summary>0語の理由(情報の帯)。</summary>
    public string? InfoMessage { get; }

    /// <summary>開始の保存に失敗した(警告の帯)。</summary>
    public string? WarningMessage { get; }

    /// <summary>設定を保存した(情報の帯)。</summary>
    public string? SavedMessage { get; }

    /// <summary>「今日 3 / 10問」(記録なしは「今日 記録なし」)。</summary>
    public string TodayText { get; }

    /// <summary>「目標まであと7問」「目標達成」、または記録なしの説明。</summary>
    public string TodayNote { get; }

    public bool TodayAchieved { get; }

    /// <summary>設定画面へ(基本設定と1日の目標。戻るとホーム)。</summary>
    public RelayCommand OpenSettingsCommand { get; }

    /// <summary>端末に英語の音声が無いときの理由(自由選択の中に出す)。</summary>
    public string? AudioUnavailableNote { get; }

    public IReadOnlyList<int> QuestionCountOptions { get; }

    public IReadOnlyList<ModeOption> ModeOptions { get; }

    public IReadOnlyList<double> FlashSecondsOptions { get; }

    /// <summary>自由選択を開いているか(ホーム内の展開。閉じると起点のボタンへフォーカスを戻すのは View)。</summary>
    public bool IsFreeChoiceOpen
    {
        get => _isFreeChoiceOpen;
        set
        {
            if (SetProperty(ref _isFreeChoiceOpen, value))
            {
                OnPropertyChanged(nameof(IsFreeChoiceClosed));
            }
        }
    }

    /// <summary>閉じているときは Enter で「おすすめ学習」、開いているときは「この条件で開始」。</summary>
    public bool IsFreeChoiceClosed => !IsFreeChoiceOpen;

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
                OnPropertyChanged(nameof(IsFlashSecondsRelevant));
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

    public double SelectedFlashSeconds
    {
        get => _selectedFlashSeconds;
        set => SetProperty(ref _selectedFlashSeconds, value);
    }

    /// <summary>端末で使えないモードを選んでいるときの理由(開始は押せない。UC-30)。</summary>
    public string? SelectedModeUnavailableNote =>
        SelectedMode.Available ? null : SelectedMode.Mode == LearningMode.Audio ? ScreenText.AudioUnavailable : ScreenText.EmptyReason(Core.Application.SelectionEmptyReason.ManualModeUnavailable);

    /// <summary>フラッシュの問題が出うるとき(手動のフラッシュ、または自動ミックスでフラッシュが使えるとき)だけ秒数を出す。</summary>
    public bool IsFlashSecondsRelevant =>
        SelectedMode.AutoMix ? _available.Contains(LearningMode.Flash) : SelectedMode.Mode == LearningMode.Flash;

    /// <summary>おすすめ学習(一操作で開始)。</summary>
    public RelayCommand RecommendedCommand { get; }

    /// <summary>苦手一覧へ(戻るとホームに戻る)。</summary>
    public RelayCommand OpenWeakListCommand { get; }

    /// <summary>語彙セット(50語・100語)の一覧へ(UI v0.3 S-06。戻るとホームに戻る)。</summary>
    public RelayCommand OpenVocabularySetsCommand { get; }

    /// <summary>自由選択の展開・閉じる。</summary>
    public RelayCommand ToggleFreeChoiceCommand { get; }

    /// <summary>自由選択の「この条件で開始」。</summary>
    public RelayCommand StartCommand { get; }

    /// <summary>画面の値をそのまま渡す。自動ミックスのときの手動モードは保存済みの値のまま。</summary>
    public PracticeChoice Choice => new(
        SelectedMode.AutoMix,
        SelectedMode.AutoMix ? _defaults.ManualMode : SelectedMode.Mode,
        SelectedQuestionCount,
        WeakOnly,
        SelectedFlashSeconds);

    internal static ModeOption[] BuildModeOptions(IReadOnlySet<LearningMode> available, PracticeChoice initial)
    {
        var options = new List<ModeOption> { new(ScreenText.AutoMix, true, initial.ManualMode) };
        foreach (var mode in Enum.GetValues<LearningMode>())
        {
            if (available.Contains(mode))
            {
                options.Add(new ModeOption(ScreenText.ModeName(mode), false, mode));
            }
            else if (mode == LearningMode.Audio || (!initial.AutoMix && initial.ManualMode == mode))
            {
                // 音声は端末で使えなくても理由付きで出す(UC-31)。保存済みの手動モードも出す(始めると Flow が ManualModeUnavailable の理由を返す)
                options.Add(new ModeOption(ScreenText.UnavailableMode(mode), false, mode, Available: false));
            }
        }

        return [.. options];
    }
}
