namespace EikenSpelling.Presentation;

/// <summary>確認・お知らせのダイアログの口。実装は Wpf(MessageBoxPrompt)。確認の初期の選択は「いいえ」。</summary>
public interface IUserPrompt
{
    /// <summary>「はい」なら真。</summary>
    bool Confirm(string message);

    void Inform(string message);
}
