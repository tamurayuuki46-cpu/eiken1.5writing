using System.Windows.Controls;

using EikenSpelling.Presentation.Screens;

namespace EikenSpelling.Wpf.Views;

/// <summary>
/// 苦手一覧。最初のフォーカスは並べ替えのボタン(UX 設計: 並べ替えに初期フォーカス)。並べ替え・絞り込みの後は押したボタンに戻す
/// (「まとめて復習」に戻すと、続けて押した Enter で練習が始まってしまう)。Esc で戻る。
/// </summary>
public partial class WeakListView : UserControl, IInitialFocus
{
    public WeakListView() => InitializeComponent();

    public void FocusInitial()
    {
        var target = DataContext is WeakListViewModel vm ? vm.InitialFocus : WeakListFocus.SortWeakFirst;
        (target switch
        {
            WeakListFocus.SortRecent => SortRecentButton,
            WeakListFocus.WeakOnly => WeakOnlyButton,
            _ => SortWeakFirstButton,
        }).Focus();
    }
}
