using System.Windows.Controls;

using EikenSpelling.Presentation.Screens;

namespace EikenSpelling.Wpf.Views;

/// <summary>セット限定の開始の確認。開始できれば「N問で開始」、0問なら「条件に戻る」にフォーカスを置く。</summary>
public partial class SetStartPreviewView : UserControl, IInitialFocus
{
    public SetStartPreviewView() => InitializeComponent();

    public void FocusInitial()
    {
        if (DataContext is SetStartPreviewViewModel { CanStart: true })
        {
            ConfirmButton.Focus();
        }
        else
        {
            CancelButton.Focus();
        }
    }
}
