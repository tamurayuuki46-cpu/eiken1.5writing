using EikenSpelling.Core.Application;
using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;
using EikenSpelling.Flow;
using EikenSpelling.Presentation.Logging;
using EikenSpelling.Presentation.Mvvm;
using EikenSpelling.Presentation.Text;

namespace EikenSpelling.Presentation.Screens;

/// <summary>
/// 出題(R8〜R10・R14、UI v0.3 の4モード)。空欄・空白だけの入力は送らない(判定は Core の SpellingJudge.IsBlank)。
/// 音声: 受け付け開始で1回読み上げ、聞き直しは ReplayCommand(Ctrl+Space)。読み上げても入力は変えない。綴りは画面に出さない。
/// フラッシュ: 段階は Flow が持つ。NotStarted なら受け付け開始で ShowFlash、Showing なら秒数の後に EndFlash、Hidden なら入力欄。
/// </summary>
public sealed class QuestionViewModel : ScreenViewModel
{
    public const string QuitConfirmMessage = "練習をやめますか?(ここまでの回答は保存されています。次の起動か「続きから」で再開できます)";

    private readonly string _word;
    private readonly double _flashSeconds;
    private readonly Guid _sessionId;
    private string _input = "";
    private string? _speechWarning;
    private IDisposable? _flashTimer;

    public QuestionViewModel(ShellViewModel shell, QuestionScreen screen, string? keptInput = null)
        : base(shell)
    {
        _input = keptInput ?? "";
        var q = screen.Question;
        _word = q.Word;
        _flashSeconds = screen.FlashSeconds;
        _sessionId = screen.SessionId;
        Number = screen.Number;
        Total = screen.Total;
        Answered = screen.Answered;
        QueueLength = screen.QueueLength;
        ProgressText = ScreenText.ProgressText(screen.Phase, screen.Number, screen.Total);
        Mode = q.Mode;
        ModeName = ScreenText.ModeName(q.Mode);
        Prompt = q.Mode switch
        {
            LearningMode.Blank => q.Blanks ?? "",
            LearningMode.Audio => ScreenText.AudioPrompt,
            LearningMode.Flash => ScreenText.FlashPrompt,
            _ => q.Japanese,
        };
        LetterCount = q.Mode == LearningMode.Blank ? q.Word.Length : null; // 音声では文字数を出さない(音声モード Q-A3)
        IsRetry = q.IsRetry;
        Phase = screen.Phase;
        PhaseName = ScreenText.PhaseName(screen.Phase);
        CanSkip = screen.CanSkip;
        CanReveal = screen.CanReveal;
        SaveFailure = screen.SaveFailure;
        Flash = screen.Flash;
        CanAnswer = screen.CanAnswer;
        AnswerCommand = Command(() => Shell.SubmitAnswer(this, Input), () => CanAnswer && !IsInputBlank);

        // 後回しにできるかは Flow(Core)が決めた CanSkip だけで決める(画面で判断しない)
        SkipCommand = Command(() => Invoke(flow => flow.Skip()), () => CanSkip && !IsSavePending);
        // 答えを見るも Flow(Core)が決めた CanReveal だけで決める(後回しの段階だけ。再練習では出ない)
        RevealCommand = Command(() => Shell.Reveal(this), () => CanReveal && !IsSavePending);
        RetrySaveCommand = Command(() => Invoke(flow => flow.RetrySave()), () => IsSavePending);
        ReplayCommand = Command(Speak, () => IsAudioMode);
        QuitCommand = Command(() =>
        {
            // フラッシュの表示中は、確認の間にタイマーで画面が替わらないよう先に止める(はいが古い画面の操作として捨てられるのを防ぐ)
            var wasShowing = _flashTimer is not null;
            _flashTimer?.Dispose();
            _flashTimer = null;
            if (Shell.Prompt.Confirm(QuitConfirmMessage))
            {
                Invoke(flow => flow.Quit());
            }
            else if (wasShowing)
            {
                Invoke(flow => flow.EndFlash()); // いいえ: 十分に見たので単語を隠して入力欄へ
            }
        }, () => !IsSavePending);
    }

    public int Number { get; }

    public int Total { get; }

    /// <summary>練習全体の採点済みの数と列の長さ(段階が変わってもバーが巻き戻らないように)。</summary>
    public int Answered { get; }

    public int QueueLength { get; }

    /// <summary>進捗バーの割合(0〜1)。練習全体の採点済みの数 / 列の長さ。</summary>
    public double Progress => QueueLength == 0 ? 0 : (double)Answered / QueueLength;

    /// <summary>段階別の進み具合の1行(「通常:全5問中の2問目」など)。</summary>
    public string ProgressText { get; }

    public LearningMode Mode { get; }

    public string ModeName { get; }

    /// <summary>日本語は意味、穴あきは穴あき表示、音声・フラッシュは案内文(綴りは入れない)。</summary>
    public string Prompt { get; }

    public bool IsBlankMode => Mode == LearningMode.Blank;

    public bool IsAudioMode => Mode == LearningMode.Audio;

    public bool IsFlashMode => Mode == LearningMode.Flash;

    /// <summary>穴あきモードの文字数(仕様 3.3)。</summary>
    public int? LetterCount { get; }

    public bool IsRetry { get; }

    public QuestionPhase Phase { get; }

    /// <summary>今の段階の名前(通常 / 後回し / 再練習)。</summary>
    public string PhaseName { get; }

    public bool IsDeferred => Phase == QuestionPhase.Deferred;

    /// <summary>「後回し」ボタンを出すか(通常の段階。保存待ち・フラッシュの表示中は出したまま押せなくする)。</summary>
    public bool IsNormalPhase => Phase == QuestionPhase.Normal;

    public FlashPhase Flash { get; }

    /// <summary>フラッシュで単語を見せている間だけ、その単語(画面の表示用。ログ・ToString・名前に出さない)。</summary>
    public string? FlashWord => Flash == FlashPhase.Showing ? _word : null;

    /// <summary>入力欄と回答のボタンを出すか(フラッシュは隠した後だけ)。</summary>
    public bool IsInputVisible => Flash is FlashPhase.None or FlashPhase.Hidden;

    /// <summary>今回答できるか(Flow の値)。</summary>
    public bool CanAnswer { get; }

    /// <summary>キー操作の案内。保存待ちの間は Enter・Esc が効かないので案内を替える。</summary>
    public string KeyHint => IsSavePending
        ? "「もう一度保存」を押してください(回答・やめるは保存の後に押せます)"
        : IsAudioMode ? "Enter で回答 ・ Ctrl+Space で聞き直す ・ Esc でやめる"
        : "Enter で回答 ・ Esc でやめる";

    /// <summary>今後回しにできるか(Flow の値。保存待ちの間は偽)。</summary>
    public bool CanSkip { get; }

    /// <summary>「答えを見る」を出せるか(Flow の値。後回しの段階だけ)。</summary>
    public bool CanReveal { get; }

    /// <summary>保存待ち(後回し・フラッシュの表示開始)の文言(Flow)。ある間は「もう一度保存」だけ押せる。</summary>
    public string? SaveFailure { get; }

    public bool IsSavePending => SaveFailure is not null;

    /// <summary>読み上げに失敗したときの帯の文言(誤答にはしない)。</summary>
    public string? SpeechWarning
    {
        get => _speechWarning;
        private set => SetProperty(ref _speechWarning, value);
    }

    public string? RetryNote => IsRetry ? "間違えた単語の再練習です(合計が増えました)" : null;

    public string Input
    {
        get => _input;
        set
        {
            if (SetProperty(ref _input, value ?? ""))
            {
                OnPropertyChanged(nameof(IsInputBlank));
                AnswerCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsInputBlank => SpellingJudge.IsBlank(Input);

    /// <summary>回答欄の文字数の上限(誤答入力の記録の上限と同じ)。</summary>
    public static int MaxInputLength => InputRules.MaxStoredInputLength;

    public RelayCommand AnswerCommand { get; }

    public RelayCommand QuitCommand { get; }

    public RelayCommand SkipCommand { get; }

    /// <summary>後回しの問題の答えを見る(不正解として記録される)。</summary>
    public RelayCommand RevealCommand { get; }

    public RelayCommand RetrySaveCommand { get; }

    /// <summary>音声を聞き直す(ボタン・Ctrl+Space)。入力欄の文字とフォーカスは変えない。</summary>
    public RelayCommand ReplayCommand { get; }

    /// <summary>テスト用(正解を打つため)。画面には出さない。</summary>
    internal string WordForTests => _word;

    public override string ToString() => $"QuestionViewModel {{ Number = {Number}, Total = {Total}, Mode = {Mode}, Input = (伏せる) }}";

    protected override void OnAcceptingStarted()
    {
        if (IsSavePending)
        {
            return; // 保存待ちの間は「もう一度保存」だけ(同じ問題の作り直しなので、読み直さない・フラッシュを始めない)
        }

        if (IsAudioMode)
        {
            Speak();
        }

        if (Flash == FlashPhase.NotStarted)
        {
            Invoke(flow => flow.ShowFlash()); // 印を保存してから見せる(Flow)
        }
        else if (Flash == FlashPhase.Showing)
        {
            _flashTimer = Shell.Scheduler.Schedule(TimeSpan.FromSeconds(_flashSeconds), () => Invoke(flow => flow.EndFlash()));
        }
    }

    protected override void OnDeactivated()
    {
        _flashTimer?.Dispose();
        _flashTimer = null;
        if (IsAudioMode)
        {
            Shell.Speech.Stop();
        }
    }

    private void Speak()
    {
        SpeechWarning = null;
        Shell.Speech.Speak(_word, reason =>
        {
            if (!IsActive)
            {
                return; // 古い画面の失敗は何もしない
            }

            SpeechWarning = ScreenText.SpeechFailed;
            Shell.Log.Write(LogLevel.Warn, "Speech", reason, _sessionId); // 理由は固定文・型名だけ(単語は書かない)
        });
    }
}
