using System.Windows;
using System.Windows.Controls;

using EikenSpelling.Presentation.Screens;

namespace EikenSpelling.Wpf.Views;

/// <summary>出題。回答欄は英字入力専用(IME 無効)、上限は誤答入力の記録と同じ文字数、貼り付けた改行は除く(R10)。</summary>
public partial class QuestionView : UserControl, IInitialFocus
{
    public QuestionView()
    {
        InitializeComponent();
        AnswerBox.MaxLength = QuestionViewModel.MaxInputLength;
        DataObject.AddPastingHandler(AnswerBox, OnPasting);
    }

    /// <summary>
    /// ふだんは回答欄、保存待ちの間は「もう一度保存」(回答欄は打ちかけの入力を残すが送れない)。
    /// フラッシュの表示中は回答欄が隠れているので単語の表示へ(フォーカスが画面の中にないと Esc が効かないため。
    /// ボタンにすると、続けて押した Enter で「やめる」が押されてしまう)。
    /// </summary>
    public void FocusInitial()
    {
        if (DataContext is QuestionViewModel { IsSavePending: true })
        {
            RetrySaveButton.Focus();
            return;
        }

        if (DataContext is QuestionViewModel { IsInputVisible: false })
        {
            FlashWordText.Focus();
            return;
        }

        AnswerBox.Focus();
        AnswerBox.CaretIndex = AnswerBox.Text.Length;
    }

    private static void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.SourceDataObject.GetData(DataFormats.UnicodeText) is not string text)
        {
            return;
        }

        var cleaned = text.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal);
        if (cleaned != text)
        {
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, cleaned);
            e.DataObject = data;
        }
    }
}
