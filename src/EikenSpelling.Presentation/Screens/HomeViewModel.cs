using EikenSpelling.Core.Models;
using EikenSpelling.Flow;
using EikenSpelling.Presentation.Mvvm;
using EikenSpelling.Presentation.Text;

namespace EikenSpelling.Presentation.Screens;

/// <summary>出題方法の選択肢(自動ミックス、または手動のモード)。Available が偽なら端末で使えない(保存済みの値を出すときだけ)。</summary>
public sealed record ModeOption(string Label, bool AutoMix, LearningMode Mode, bool Available = true);

/// <summary>ホーム(R6・R7)。学習状況5項目と練習設定。範囲の検査・0語の判断は Flow と Core。</summary>
public sealed class HomeViewModel : ScreenViewModel
{
    private readonly PracticeChoice _defaults;
    private int _selectedQuestionCount;
    private ModeOption _selectedMode;
    private bool _weakOnly;

    public HomeViewModel(ShellViewModel shell, HomeScreen screen, PracticeChoice? previous = null)
        : base(shell)
    {
        _defaults = screen.Defaults;
        var initial = previous ?? screen.Defaults;
        Unseen = screen.Status.Unseen;
        Normal = screen.Status.Normal;
        Weak = screen.Status.Weak;
        Mastered = screen.Status.Mastered;
        ReviewDue = screen.Status.ReviewDue;
        InfoMessage = ScreenText.EmptyReason(screen.EmptyReason);
        WarningMessage = screen.Message;

        QuestionCountOptions = [.. screen.QuestionCountChoices.Append(initial.QuestionCount).Distinct().Order()];
        ModeOptions = BuildModeOptions(shell.AvailableModes, initial);
        _selectedQuestionCount = initial.QuestionCount;
        _selectedMode = initial.AutoMix ? ModeOptions[0] : ModeOptions.First(o => !o.AutoMix && o.Mode == initial.ManualMode);
        _weakOnly = initial.WeakWordsOnly;
        StartCommand = Command(() => Invoke(flow => flow.StartPractice(Choice)));
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

    public IReadOnlyList<int> QuestionCountOptions { get; }

    public IReadOnlyList<ModeOption> ModeOptions { get; }

    public int SelectedQuestionCount
    {
        get => _selectedQuestionCount;
        set => SetProperty(ref _selectedQuestionCount, value);
    }

    public ModeOption SelectedMode
    {
        get => _selectedMode;
        set => SetProperty(ref _selectedMode, value);
    }

    public bool WeakOnly
    {
        get => _weakOnly;
        set => SetProperty(ref _weakOnly, value);
    }

    public RelayCommand StartCommand { get; }

    /// <summary>画面の値をそのまま渡す。自動ミックスのときの手動モードは保存済みの値のまま、フラッシュ秒数も保存済みの値。</summary>
    public PracticeChoice Choice => new(
        SelectedMode.AutoMix,
        SelectedMode.AutoMix ? _defaults.ManualMode : SelectedMode.Mode,
        SelectedQuestionCount,
        WeakOnly,
        _defaults.FlashSeconds);

    private static ModeOption[] BuildModeOptions(IReadOnlySet<LearningMode> available, PracticeChoice initial)
    {
        var options = new List<ModeOption> { new(ScreenText.AutoMix, true, initial.ManualMode) };
        foreach (var mode in Enum.GetValues<LearningMode>())
        {
            if (available.Contains(mode))
            {
                options.Add(new ModeOption(ScreenText.ModeName(mode), false, mode));
            }
            else if (!initial.AutoMix && initial.ManualMode == mode)
            {
                // 保存済みの手動モードが端末で使えなくても出す(始めると Flow が ManualModeUnavailable の理由を返す)
                options.Add(new ModeOption(ScreenText.UnavailableMode(mode), false, mode, Available: false));
            }
        }

        return [.. options];
    }
}
