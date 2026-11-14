using System.Windows;
using System.Windows.Controls;

namespace EikenSpelling.Wpf.Views;

public partial class ResultView : UserControl, IInitialFocus
{
    /// <summary>これより狭ければカードを縦に並べる(UI 設計 6章)。</summary>
    private const double StackBelow = 640;

    public ResultView() => InitializeComponent();

    public void FocusInitial() => HomeButton.Focus();

    private void OnCardsSizeChanged(object sender, SizeChangedEventArgs e) =>
        Cards.Columns = e.NewSize.Width < StackBelow ? 1 : 3;
}
