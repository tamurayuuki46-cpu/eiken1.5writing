using EikenSpelling.Core.Models;
using EikenSpelling.Flow;
using EikenSpelling.Flow.Tests;
using EikenSpelling.Persistence;
using EikenSpelling.Persistence.Tests;
using EikenSpelling.Presentation.Logging;
using EikenSpelling.Presentation.Screens;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Presentation.Tests;

/// <summary>
/// 手で進める時計。GetUtcNow(Flow の日付)と GetTimestamp(受け付け開始のゲート)を同じだけ進める。
/// 固定時間の待ちを使わずに 0.3 秒の境界を確かめる(testing.md)。
/// </summary>
internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    private long _ticks;

    public DateTimeOffset Now { get; private set; } = now;

    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();

    /// <summary>端末のローカルのタイムゾーン(日別の実績の日付)。テストの日付を固定するため +9:00。</summary>
    public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone("Test+9", TimeSpan.FromHours(9), "Test+9", "Test+9");

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _ticks;

    public void Advance(TimeSpan by)
    {
        _ticks += by.Ticks;
        Now += by;
    }
}

/// <summary>決まった答えを返す確認ダイアログ。聞かれた文言を記録する。</summary>
internal sealed class ScriptedPrompt : IUserPrompt
{
    public bool Answer { get; set; }

    public List<string> Asked { get; } = [];

    public List<string> Informed { get; } = [];

    /// <summary>確認ダイアログが開いている間に起きること(タイマーの発火など)を差し込む。</summary>
    public Action? WhileOpen { get; set; }

    public bool Confirm(string message)
    {
        Asked.Add(message);
        WhileOpen?.Invoke();
        return Answer;
    }

    public void Inform(string message) => Informed.Add(message);
}

/// <summary>読み上げを記録する偽の実装。FailNext で次の読み上げを失敗させる。</summary>
internal sealed class FakeSpeech : Speech.ISpeechPlayer
{
    public bool IsAvailable { get; set; } = true;

    public List<string> Spoken { get; } = [];

    public int Stops { get; private set; }

    public bool FailNext { get; set; }

    /// <summary>真なら失敗の通知をすぐ呼ばずに取っておく(遅れて届く失敗。FireDeferred で呼ぶ)。</summary>
    public bool DeferFailure { get; set; }

    private Action<string>? _deferred;

    public void Speak(string text, Action<string> onFailed)
    {
        Spoken.Add(text);
        if (DeferFailure)
        {
            _deferred = onFailed;
        }
        else if (FailNext)
        {
            FailNext = false;
            onFailed("TestFailure");
        }
    }

    public void FireDeferred() => _deferred?.Invoke("TestFailure");

    public void Stop() => Stops++;
}

/// <summary>手で進めるスケジューラ。RunDue で、指定した経過時間までに来た予定を実行する。</summary>
internal sealed class ManualScheduler : Scheduling.IUiScheduler
{
    private readonly List<(TimeSpan Delay, Action Action, Cancel Handle)> _items = [];

    public IReadOnlyList<TimeSpan> Delays => [.. _items.Where(i => !i.Handle.Cancelled).Select(i => i.Delay)];

    public IDisposable Schedule(TimeSpan delay, Action action)
    {
        var handle = new Cancel();
        _items.Add((delay, action, handle));
        return handle;
    }

    /// <summary>取り消されていない予定をすべて実行する(時間が来たとみなす)。</summary>
    public void RunAll()
    {
        foreach (var item in _items.ToList())
        {
            _items.Remove(item);
            if (!item.Handle.Cancelled)
            {
                item.Action();
            }
        }
    }

    public sealed class Cancel : IDisposable
    {
        public bool Cancelled { get; private set; }

        public void Dispose() => Cancelled = true;
    }
}

/// <summary>書いた行を覚えるだけのログ。</summary>
internal sealed class RecordingLog : IAppLog
{
    public List<(LogLevel Level, string Op, string? Reason, Guid? SessionId)> Lines { get; } = [];

    public void Write(LogLevel level, string op, string? reason = null, Guid? sessionId = null) => Lines.Add((level, op, reason, sessionId));
}

/// <summary>1つのテストの環境。一時フォルダの本物のファイルで Flow を動かし、ShellViewModel を作る。</summary>
internal sealed class ShellEnv : IDisposable
{
    public static readonly DateTimeOffset T0 = new(2026, 11, 1, 10, 0, 0, TimeSpan.FromHours(9));

    private int _ids;

    public TempDir Dir { get; } = new();

    public ManualTimeProvider Time { get; } = new(T0);

    public SwitchableFiles Files { get; } = new();

    public ScriptedPrompt Prompt { get; } = new();

    public RecordingLog Log { get; } = new();

    public string? WordsJson { get; set; } = Words.Json(Words.Five);

    public HashSet<LearningMode> Modes { get; set; } = [Japanese, Blank];

    public FakeSpeech Speech { get; } = new();

    public ManualScheduler Scheduler { get; } = new();

    public string? DataDirectory { get; set; }

    public PracticeFlow StartFlow() => PracticeFlow.Start(new FlowOptions
    {
        DataDirectory = DataDirectory ?? Dir.Path,
        WordSetJson = WordsJson,
        AvailableModes = Modes,
        Time = Time,
        Random = new Random(1),
        NewSessionId = () => new Guid(++_ids, 0, 0, new byte[8]),
        Files = Files,
        // 本物のファイルを置き換えるので、ウイルス対策の検査などで一瞬ロックされても再試行で抜けられるよう、わずかに待つ
        // (0 だと3回の再試行が一瞬で終わる。全体実行で1回だけ落ちたことがある。tasklist のメモ)
        RetryDelay = TimeSpan.FromMilliseconds(20),
    });

    /// <summary>ShellViewModel を作り、最初の画面の受け付けを開始する(窓が初期フォーカスを移した後と同じ)。</summary>
    public ShellViewModel Start()
    {
        var shell = new ShellViewModel(StartFlow(), new ShellOptions(Modes, Prompt, Log, Time, Speech, Scheduler));
        shell.Current.BeginAccepting();
        return shell;
    }

    public string? ProgressJsonText => Dir.Read(ProgressRepository.FileName);

    public void Dispose() => Dir.Dispose();
}

internal static class ShellEnvJson
{
    public static string EmptyProgress => FlowEnv.EmptyProgressJson;
}

internal static class ShellExtensions
{
    public static readonly TimeSpan Open = TimeSpan.FromMilliseconds(300);

    public static T As<T>(this ShellViewModel shell) where T : ScreenViewModel => Assert.IsType<T>(shell.Current);

    /// <summary>
    /// 正当な操作として実行する: 受け付けを開始し、時計を 0.3 秒進めてから実行する(ゲートで止まる空振りを防ぐ)。
    /// 実行後の新しい画面の受け付けも開始する(窓が初期フォーカスを移した後と同じ)。
    /// </summary>
    public static void Run(this ShellViewModel shell, ManualTimeProvider time, Func<ScreenViewModel, Mvvm.RelayCommand> pick)
    {
        var screen = shell.Current;
        screen.BeginAccepting();
        time.Advance(Open);
        pick(screen).Execute(null);
        shell.Current.BeginAccepting();
    }

    /// <summary>ホームから既定の5問(自動ミックス)で始める。</summary>
    public static QuestionViewModel StartPractice(this ShellViewModel shell, ManualTimeProvider time, int count = 5)
    {
        var home = shell.As<HomeViewModel>();
        home.SelectedQuestionCount = count;
        shell.Run(time, s => ((HomeViewModel)s).StartCommand);
        return shell.As<QuestionViewModel>();
    }

    /// <summary>今の問題に正答(または誤答)して判定画面にする。</summary>
    public static FeedbackViewModel AnswerCurrent(this ShellViewModel shell, ManualTimeProvider time, bool correct)
    {
        var q = shell.As<QuestionViewModel>();
        q.Input = correct ? q.WordForTests : q.WordForTests + "x";
        shell.Run(time, s => ((QuestionViewModel)s).AnswerCommand);
        return shell.As<FeedbackViewModel>();
    }
}
