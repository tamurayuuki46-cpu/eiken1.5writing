using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using EikenSpelling.Presentation;

namespace EikenSpelling.Wpf;

/// <summary>
/// 1枚の窓(design 4.9)。画面は ShellViewModel.Current を DataTemplate で切り替える。
/// ・Current が変わるたびに、描画の後で初期フォーカスへ移し、そこから受け付け開始のゲートを数え始める
/// ・押しっぱなしの Enter・Esc は止める
/// ・保存できていない回答があるときだけ、閉じる前に確認する(終了ボタン・例外では確認しない)
/// </summary>
public partial class MainWindow : Window
{
    private const string UnsavedCloseMessage = "この回答はまだ保存できていません。閉じると、この1問の回答は記録されません。閉じますか?";

    private readonly ShellViewModel _shell;
    private bool _closeWithoutConfirmation;

    public MainWindow(ShellViewModel shell)
    {
        InitializeComponent();
        _shell = shell;
        DataContext = shell;
        FitToWorkArea();
        shell.PropertyChanged += OnShellPropertyChanged;
        Loaded += (_, _) => ScheduleInitialFocus();
    }

    /// <summary>今の画面の初期フォーカスへ戻す(確認ダイアログ・通知を閉じた後)。</summary>
    public void FocusCurrentScreen() => FindView()?.FocusInitial();

    public void AllowCloseWithoutConfirmation() => _closeWithoutConfirmation = true;

    public void CloseWithoutConfirmation()
    {
        _closeWithoutConfirmation = true;
        Close();
    }

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true; // 前に出ないときのために一瞬だけ
        Topmost = false;
        Focus();
    }

    /// <summary>既定 1000×680・最小 800×560。どちらも作業領域(タスクバーを除く)で頭打ち(H3)。</summary>
    private void FitToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        MinWidth = Math.Min(800, area.Width);
        MinHeight = Math.Min(560, area.Height);
        Width = Math.Min(1000, area.Width);
        Height = Math.Min(680, area.Height);
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.Current))
        {
            ScheduleInitialFocus();
        }
    }

    /// <summary>
    /// 描画・Loaded の後(Loaded 優先度)で初期フォーカスを移し、受け付けを開始する。
    /// Loaded だけに頼らない(同じ型の画面に替わると View が作り直されず Loaded が来ないため)。
    /// </summary>
    private void ScheduleInitialFocus()
    {
        var screen = _shell.Current;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (!ReferenceEquals(screen, _shell.Current))
            {
                return; // その間に画面が替わった(新しい画面の分がもう一度積まれている)
            }

            FindView()?.FocusInitial();
            screen.BeginAccepting();
        });
    }

    private IInitialFocus? FindView()
    {
        if (VisualTreeHelper.GetChildrenCount(ScreenHost) == 0)
        {
            return null;
        }

        return VisualTreeHelper.GetChild(ScreenHost, 0) is ContentPresenter presenter && VisualTreeHelper.GetChildrenCount(presenter) > 0
            ? VisualTreeHelper.GetChild(presenter, 0) as IInitialFocus
            : null;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.IsRepeat && (e.Key == Key.Enter || e.Key == Key.Escape))
        {
            e.Handled = true; // 押しっぱなしで判定を飛ばす・確認を開き直すのを防ぐ
        }
    }

    private void OnDismissNotice(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string notice })
        {
            _shell.DismissNotice(notice);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, FocusCurrentScreen);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeWithoutConfirmation || !_shell.HasUnsavedAnswer)
        {
            return;
        }

        var answer = MessageBox.Show(this, UnsavedCloseMessage, "EikenSpelling", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            FocusCurrentScreen();
        }
    }
}
