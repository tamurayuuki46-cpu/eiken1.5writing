using System.Windows.Controls;

using EikenSpelling.Presentation.Screens;

namespace EikenSpelling.Wpf.Views;

public partial class FeedbackView : UserControl, IInitialFocus
{
    public FeedbackView() => InitializeComponent();

    /// <summary>保存済みなら「次へ / 結果を見る」、保存失敗中は「もう一度保存」。</summary>
    public void FocusInitial()
    {
        if (DataContext is FeedbackViewModel { Saved: false })
        {
            RetrySaveButton.Focus();
        }
        else
        {
            NextButton.Focus();
        }
    }
}
