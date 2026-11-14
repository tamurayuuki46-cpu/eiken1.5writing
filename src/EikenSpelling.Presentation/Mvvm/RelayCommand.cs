using System.Windows.Input;

namespace EikenSpelling.Presentation.Mvvm;

/// <summary>
/// 手書きのコマンド。WPF の CommandManager に頼らず、状態が変わったら呼び出し側が RaiseCanExecuteChanged を出す。
/// 画面を知らない汎用の部品(受け付け開始のゲートは ScreenViewModel が実行の処理を包んで入れる)。
/// </summary>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            execute();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
