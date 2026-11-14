using System.IO;
using System.Windows;
using System.Windows.Threading;

using EikenSpelling.Core.Models;
using EikenSpelling.Flow;
using EikenSpelling.Presentation;
using EikenSpelling.Presentation.Logging;

namespace EikenSpelling.Wpf;

/// <summary>
/// 合成の起点(design 4.1)。二重起動の防止 → ログ → 未処理の例外の受け口 → 保存先 → 単語データ → Flow → 窓。
/// 起動だけでは progress.json・settings.json を書かない(Flow の約束)。
/// </summary>
public partial class App : Application
{
    /// <summary>骨組みで使える出題モード(日本語・穴あき。音声・フラッシュは次のタスク)。</summary>
    private static readonly IReadOnlySet<LearningMode> Modes = new HashSet<LearningMode> { LearningMode.Japanese, LearningMode.Blank };

    private const string CrashMessage = "問題が起きたため終了します。最後に保存できた問題までの記録は残っています。";

    private SingleInstance? _single;
    private IAppLog _log = NullLog.Instance;
    private MainWindow? _window;
    private int _crashing;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 未処理の例外の受け口は最初に登録する(二重起動の防止などで起きた例外でも既定の例外ダイアログにしない。ログは後で用意される)
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        // 1. 二重起動の防止
        _single = SingleInstance.Acquire();
        if (!_single.IsFirst)
        {
            _single.SignalFirst();
            Shutdown();
            return;
        }

        // 2. ログ(取れなければログなしで続ける)
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var fileLog = new FileLog(string.IsNullOrEmpty(local) ? null : Path.Combine(local, "EikenSpelling", "logs"), TimeProvider.System);
        fileLog.DeleteOld();
        _log = fileLog;

        // 4. 保存先
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var arguments = StartupArguments.Parse(e.Args, string.IsNullOrEmpty(roaming) ? null : Path.Combine(roaming, "EikenSpelling"));
        if (arguments.HadUnknown)
        {
            _log.Write(LogLevel.Warn, "startup", "unknown-argument");
        }

        // 5. 単語データ(無い・読めないなら null → Flow が WordSetInvalid)
        var wordsJson = ReadWords();

        // 6〜7. Flow と窓
        var flow = PracticeFlow.Start(new FlowOptions
        {
            DataDirectory = arguments.DataDirectory,
            WordSetJson = wordsJson,
            AvailableModes = Modes,
        });
        var prompt = new MessageBoxPrompt(() => _window, () => _window?.FocusCurrentScreen());
        var shell = new ShellViewModel(flow, new ShellOptions(Modes, prompt, _log, TimeProvider.System));
        _window = new MainWindow(shell);
        shell.ExitRequested += (_, _) => _window.CloseWithoutConfirmation();
        _single.ListenForActivation(() => Dispatcher.BeginInvoke(() => _window?.BringToFront()));
        MainWindow = _window;
        _window.Show();
        _window.Activate(); // 起動直後に前面へ(最初の Enter が窓の前面化に使われないように。B3)
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _single?.Dispose();
        base.OnExit(e);
    }

    private string? ReadWords()
    {
        try
        {
            return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "words.json"), System.Text.Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write(LogLevel.Warn, "words", ex.GetType().Name); // 種類だけ(パスは書かない)
            return null;
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true; // 既定の例外ダイアログを出さない
        Crash(e.Exception, onUiThread: true);
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // UI スレッド以外から来ることがある。ログを先に書き、ダイアログは UI スレッドで出せるときだけ
        Crash(e.ExceptionObject as Exception, onUiThread: Dispatcher.CheckAccess());
    }

    private void Crash(Exception? exception, bool onUiThread)
    {
        if (Interlocked.Exchange(ref _crashing, 1) == 1)
        {
            return; // 終了の途中でもう一度来ても、ダイアログは1回だけ
        }

        _log.Write(LogLevel.Error, "unhandled", exception is null ? "unknown" : ExceptionInfo.Describe(exception));
        if (onUiThread)
        {
            ShowCrashAndExit();
        }
        else
        {
            // 同期の Invoke は UI スレッドが止まっていると終了ごと止まるので、非同期で頼むだけにする(出せなくても終了は止めない)
            Dispatcher.BeginInvoke(ShowCrashAndExit);
        }
    }

    private void ShowCrashAndExit()
    {
        if (_window is not null)
        {
            _window.AllowCloseWithoutConfirmation(); // 例外の後に「閉じますか?」を出さない
            _window.IsEnabled = false; // ダイアログの間に画面を操作させない(続行も保存もしない。H1)
        }

        // IUserPrompt を通さず直接出す(閉じた後にフォーカスを戻す処理を動かさないため。decisions に記録)
        MessageBox.Show(CrashMessage, "EikenSpelling", MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(1);
    }
}
