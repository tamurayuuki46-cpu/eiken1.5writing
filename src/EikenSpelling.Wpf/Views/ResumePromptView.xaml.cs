using System.Windows.Controls;

using EikenSpelling.Presentation.Screens;

namespace EikenSpelling.Wpf.Views;

public partial class ResumePromptView : UserControl, IInitialFocus
{
    public ResumePromptView() => InitializeComponent();

    /// <summary>再開できれば「続きから」、できなければ「終了して新しく始める」。</summary>
    public void FocusInitial()
    {
        if (DataContext is ResumePromptViewModel { CanResume: true })
        {
            ResumeButton.Focus();
        }
        else
        {
            DiscardButton.Focus();
        }
    }
}
