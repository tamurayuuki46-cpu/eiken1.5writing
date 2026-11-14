using System.Collections.ObjectModel;

using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;
using EikenSpelling.Flow;
using EikenSpelling.Presentation.Logging;
using EikenSpelling.Presentation.Mvvm;
using EikenSpelling.Presentation.Screens;

namespace EikenSpelling.Presentation;

/// <summary>ShellViewModel に渡すもの。AvailableModes は Flow に渡したものと同じ集合。</summary>
public sealed record ShellOptions(IReadOnlySet<LearningMode> AvailableModes, IUserPrompt Prompt, IAppLog Log, TimeProvider Time);

/// <summary>判定画面に出す直前の回答の値(FeedbackScreen には無い)。Input は正規化した文字列。ログ・ToString に出さない。</summary>
public sealed class LastAnswer(int number, int total, string input)
{
    public int Number { get; } = number;

    public int Total { get; } = total;

    public string Input { get; } = input;

    public override string ToString() => $"LastAnswer {{ Number = {Number}, Total = {Total}, Input = (伏せる) }}";
}

/// <summary>
/// 画面の切り替えの中心(design 4.3)。Flow を1つ持ち、今の画面の ViewModel・通知・終了要求を公開する。
/// どの画面へ行くかは Flow の Screen の型だけで決まる(ここでは判断しない)。1スレッド(UI スレッド)から使う。
/// </summary>
public sealed class ShellViewModel : ObservableObject
{
    private readonly PracticeFlow _flow;
    private readonly ShellOptions _options;
    private ScreenViewModel _current = null!;

    public ShellViewModel(PracticeFlow flow, ShellOptions options)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(options);
        _flow = flow;
        _options = options;
        Refresh();
    }

    /// <summary>「終了」が押された。Wpf が確認なしで閉じる。</summary>
    public event EventHandler? ExitRequested;

    public ScreenViewModel Current
    {
        get => _current;
        private set => SetProperty(ref _current, value);
    }

    /// <summary>通知の一覧。閉じるまで残す(画面が変わっても消さない。R5)。</summary>
    public ObservableCollection<string> Notices { get; } = [];

    /// <summary>保存できていない回答がある(閉じる前の確認に使う。R25)。</summary>
    public bool HasUnsavedAnswer => _flow.Screen is FeedbackScreen { Saved: false };

    public LastAnswer? LastAnswer { get; private set; }

    internal TimeProvider Time => _options.Time;

    internal IUserPrompt Prompt => _options.Prompt;

    internal IReadOnlySet<LearningMode> AvailableModes => _options.AvailableModes;

    public void DismissNotice(string notice) => Notices.Remove(notice);

    /// <summary>今の画面からの操作だけを Flow に渡す。古い画面からの操作は何もしない(例外にしない)。</summary>
    internal void Invoke(ScreenViewModel sender, Action<PracticeFlow> operation)
    {
        if (!IsCurrent(sender))
        {
            return;
        }

        operation(_flow);
        Refresh();
    }

    /// <summary>回答を送る。直前の回答の値は、今の画面からの操作と確かめた後にだけ覚える。</summary>
    internal void SubmitAnswer(QuestionViewModel sender, string input)
    {
        if (!IsCurrent(sender))
        {
            return;
        }

        LastAnswer = new LastAnswer(sender.Number, sender.Total, SpellingJudge.Normalize(input));
        _flow.Answer(input);
        Refresh();
    }

    internal void RequestExit(ScreenViewModel sender)
    {
        if (IsCurrent(sender))
        {
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool IsCurrent(ScreenViewModel sender) => ReferenceEquals(sender, _current) && sender.IsActive;

    private void Refresh()
    {
        var screen = _flow.Screen;
        if (screen.Diagnostic is { } diagnostic)
        {
            _options.Log.Write(LogLevel.Error, screen.GetType().Name, diagnostic, (screen as FeedbackScreen)?.SessionId);
        }

        // 今の Flow では同じ通知が2回届く経路は無い(通知は起動・再読込の最初の画面と StartFresh の後だけ)。
        // 将来の変更で重なっても同じ文を並べないための備え
        foreach (var notice in screen.Notices)
        {
            if (!Notices.Contains(notice))
            {
                Notices.Add(notice);
            }
        }

        if (screen is not FeedbackScreen)
        {
            LastAnswer = null;
        }

        var previous = _current;
        var next = ScreenViewModelFactory.Create(this, screen, previous);
        previous?.Deactivate();
        Current = next;
    }
}
