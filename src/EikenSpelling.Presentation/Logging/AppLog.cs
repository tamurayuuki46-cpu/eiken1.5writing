namespace EikenSpelling.Presentation.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

/// <summary>
/// ログの口(logging.md)。キーと値の1行。reason は固定文(Flow・Persistence の Diagnostic など)だけを渡し、
/// パス・誤答入力・画面や ViewModel の状態・SpellingDiff は渡さない。
/// </summary>
public interface IAppLog
{
    void Write(LogLevel level, string op, string? reason = null, Guid? sessionId = null);
}

/// <summary>ログを用意できなかったとき用。何もしない。</summary>
public sealed class NullLog : IAppLog
{
    public static readonly NullLog Instance = new();

    public void Write(LogLevel level, string op, string? reason = null, Guid? sessionId = null)
    {
    }
}
