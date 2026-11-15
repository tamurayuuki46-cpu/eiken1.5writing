using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

using EikenSpelling.Presentation.Screens;

namespace EikenSpelling.Wpf.Views;

/// <summary>ホーム。自由選択を開いたら最初の項目へ、閉じたら起点の「自由選択」へフォーカスを移す(UI v0.3 S-02)。</summary>
public partial class HomeView : UserControl, IInitialFocus
{
    private HomeViewModel? _viewModel;

    public HomeView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // 設定の保存の成功をナレーターに知らせる(フォーカスは「おすすめ学習」に移るため、帯は読まれない)
        Loaded += (_, _) => LiveRegion.Announce(SavedText, _viewModel?.SavedMessage is not null);
    }

    public void FocusInitial()
    {
        if (_viewModel?.IsFreeChoiceOpen == true)
        {
            StartButton.Focus();
        }
        else
        {
            RecommendedButton.Focus();
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as HomeViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HomeViewModel.IsFreeChoiceOpen) || _viewModel is null)
        {
            return;
        }

        var open = _viewModel.IsFreeChoiceOpen;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => (open ? (Control)CountBox : FreeChoiceButton).Focus());
    }
}
