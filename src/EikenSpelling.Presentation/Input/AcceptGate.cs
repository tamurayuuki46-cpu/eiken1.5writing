namespace EikenSpelling.Presentation.Input;

/// <summary>
/// 受け付け開始のゲート(design 4.3、UX 設計 Q-UX3 改訂)。画面が今の画面になり、窓が初期フォーカスを移した後(Begin)から
/// 一定時間はコマンドを通さない。保存中に溜まったクリックや素早い Enter が、次の画面のボタンを押すのを防ぐ。
/// 時計の巻き戻しに影響されないよう、GetTimestamp の経過時間で測る。
/// </summary>
public sealed class AcceptGate(TimeProvider time)
{
    /// <summary>受け付けない時間。画面の操作の定数(学習ルールではないので LearningSettings に置かない)。</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(300);

    private long? _startedAt;

    /// <summary>数え始める。2回目以降は何もしない(最初の1回だけ数える)。</summary>
    public void Begin() => _startedAt ??= time.GetTimestamp();

    /// <summary>Begin の前は時間に関係なく偽。</summary>
    public bool IsOpen => _startedAt is long started && time.GetElapsedTime(started) >= Delay;
}
