using System.Windows.Controls;
using System.Windows.Input;

using EikenSpelling.Presentation.Screens;

namespace EikenSpelling.Wpf.Views;

/// <summary>
/// 語彙セット。最初のフォーカスは表示単位のボタン(開いた直後の Enter で練習が始まらないように)。
/// ページ送り・単位切替の後は押したボタンへ戻す。そのボタンが使えなく(先頭・末尾)なったら、反対向きの移動、
/// それも無ければ表示単位へ移す(「このセットで始める」へは移さない: ページ送りの Enter が練習開始にならないように)。
/// 一覧は 50〜100 行あるので、PageUp / PageDown / Home / End で一覧をスクロールできる(フォーカスは変えない)。
/// Esc でホームへ戻る。
/// </summary>
public partial class VocabularySetView : UserControl, IInitialFocus
{
    public VocabularySetView()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public void FocusInitial()
    {
        var focus = DataContext is VocabularySetViewModel vm ? vm.InitialFocus : VocabularySetFocus.Unit;
        var unit = DataContext is VocabularySetViewModel { IsUnit50Selected: false } ? Unit100Button : Unit50Button;
        var target = focus switch
        {
            VocabularySetFocus.Previous => First(PreviousButton, NextButton, unit),
            VocabularySetFocus.Next => First(NextButton, PreviousButton, unit),
            VocabularySetFocus.Start => First(StartButton, unit),
            _ => unit,
        };
        target.Focus();
    }

    private static Button First(params Button[] candidates) => candidates.FirstOrDefault(b => b.IsEnabled) ?? candidates[^1];

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 入力部品(コンボ・チェック)の中の操作は、その部品に任せる
        if (e.OriginalSource is ComboBox or ComboBoxItem or CheckBox || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.PageDown:
                ListScroll.PageDown();
                break;
            case Key.PageUp:
                ListScroll.PageUp();
                break;
            case Key.Home:
                ListScroll.ScrollToTop();
                break;
            case Key.End:
                ListScroll.ScrollToBottom();
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
