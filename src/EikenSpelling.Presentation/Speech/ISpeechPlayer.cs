namespace EikenSpelling.Presentation.Speech;

/// <summary>
/// 英単語の読み上げの差し替え口(音声モード design 1章)。Wpf が System.Speech で実装し、テストは偽の実装を使う。
/// いつ読むかは QuestionViewModel が決め、どう読むか(声の選択・非同期の再生)は実装が持つ。
/// </summary>
public interface ISpeechPlayer
{
    /// <summary>この端末で英語の読み上げが使えるか(起動時に決まる)。</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 読み上げる。前の読み上げは止める。すぐに戻る(読み上げ中も入力を妨げない)。
    /// 失敗したら onFailed を UI スレッドで呼ぶ(引数は例外の型名などの固定文。読み上げる単語は入れない)。
    /// </summary>
    void Speak(string text, Action<string> onFailed);

    /// <summary>読み上げを止める(画面が替わったとき)。読んでいなければ何もしない。</summary>
    void Stop();
}

/// <summary>読み上げの無い実装(音声の無い端末・テストの既定)。</summary>
public sealed class NoSpeechPlayer : ISpeechPlayer
{
    public static readonly NoSpeechPlayer Instance = new();

    public bool IsAvailable => false;

    public void Speak(string text, Action<string> onFailed) => onFailed("unavailable");

    public void Stop()
    {
    }
}
