using System.Windows.Controls;

namespace EikenSpelling.Wpf.Views;

public partial class HomeView : UserControl, IInitialFocus
{
    public HomeView() => InitializeComponent();

    public void FocusInitial() => StartButton.Focus();
}
