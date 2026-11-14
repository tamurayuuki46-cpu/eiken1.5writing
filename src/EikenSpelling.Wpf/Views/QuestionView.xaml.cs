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

    public void FocusInitial()
    {
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
