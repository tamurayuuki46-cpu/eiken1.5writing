using System.Windows.Controls;

using EikenSpelling.Presentation.Screens;

namespace EikenSpelling.Wpf.Views;

public partial class FatalView : UserControl, IInitialFocus
{
    public FatalView() => InitializeComponent();

    public void FocusInitial()
    {
        if (DataContext is FatalViewModel { CanReload: true })
        {
            ReloadButton.Focus();
        }
        else
        {
            ExitButton.Focus();
        }
    }
}
