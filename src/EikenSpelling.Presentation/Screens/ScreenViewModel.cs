using EikenSpelling.Presentation.Input;
using EikenSpelling.Presentation.Mvvm;

namespace EikenSpelling.Presentation.Screens;

/// <summary>
/// 画面の ViewModel の基底(design 4.3)。
/// ・IsActive: 今の画面でなくなったら偽にし、全コマンドを無効にする(古い画面の操作・連打で Flow の例外にしない)
/// ・受け付け開始のゲート: BeginAccepting から 0.3 秒未満は、どのコマンドも何もしない(Enter・クリックの区別なし)
/// コマンドは必ず <see cref="Command"/> で作る(ゲートを通すため。テストで全コマンドを確かめる)。
/// </summary>
public abstract class ScreenViewModel : ObservableObject
{
    private readonly List<RelayCommand> _commands = [];
    private readonly AcceptGate _gate;

    protected ScreenViewModel(ShellViewModel shell)
    {
        Shell = shell;
        _gate = new AcceptGate(shell.Time);
    }

    public bool IsActive { get; private set; } = true;

    /// <summary>この画面の全コマンド(テストでゲートの付け忘れを確かめる)。</summary>
    public IReadOnlyList<RelayCommand> Commands => _commands;

    protected ShellViewModel Shell { get; }

    /// <summary>窓が初期フォーカスを移した後に呼ぶ。最初の1回だけ数え始める。</summary>
    public void BeginAccepting() => _gate.Begin();

    internal void Deactivate()
    {
        IsActive = false;
        RaiseCommandsChanged();
    }

    /// <summary>ゲートと IsActive で包んだコマンドを作る。</summary>
    protected RelayCommand Command(Action execute, Func<bool>? canExecute = null)
    {
        var command = new RelayCommand(
            () =>
            {
                if (IsActive && _gate.IsOpen && (canExecute?.Invoke() ?? true))
                {
                    execute();
                }
            },
            () => IsActive && (canExecute?.Invoke() ?? true));
        _commands.Add(command);
        return command;
    }

    protected void RaiseCommandsChanged()
    {
        foreach (var command in _commands)
        {
            command.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Flow に操作を渡す(今の画面のときだけ)。行き先は Flow が決める。</summary>
    protected void Invoke(Action<Flow.PracticeFlow> operation) => Shell.Invoke(this, operation);
}
