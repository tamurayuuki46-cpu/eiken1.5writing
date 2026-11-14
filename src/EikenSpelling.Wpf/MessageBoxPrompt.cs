using System.Windows;

using EikenSpelling.Presentation;

namespace EikenSpelling.Wpf;

/// <summary>
/// 確認・お知らせのダイアログ(IUserPrompt の実装)。初期の選択は「いいえ」。
/// 閉じた後は、今の画面の初期フォーカスへ戻す(design 4.9。やめる → いいえ → 回答欄)。
/// 骨組みでは MessageBox を使う(UI 設計 4章の「ダイアログ内だけ Danger 地」は再現しない。decisions に記録)。
/// </summary>
internal sealed class MessageBoxPrompt(Func<Window?> owner, Action restoreFocus) : IUserPrompt
{
    private const string Caption = "EikenSpelling";

    public bool Confirm(string message)
    {
        var window = owner();
        var result = window is null
            ? MessageBox.Show(message, Caption, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
            : MessageBox.Show(window, message, Caption, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        restoreFocus();
        return result == MessageBoxResult.Yes;
    }

    public void Inform(string message)
    {
        var window = owner();
        if (window is null)
        {
            MessageBox.Show(message, Caption, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(window, message, Caption, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        restoreFocus();
    }
}
