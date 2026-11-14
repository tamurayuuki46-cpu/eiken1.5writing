using System.Windows.Controls;

namespace EikenSpelling.Wpf.Views;

public partial class BothCorruptView : UserControl, IInitialFocus
{
    public BothCorruptView() => InitializeComponent();

    /// <summary>破壊的でない「終了」に初期フォーカス。</summary>
    public void FocusInitial() => ExitButton.Focus();
}
