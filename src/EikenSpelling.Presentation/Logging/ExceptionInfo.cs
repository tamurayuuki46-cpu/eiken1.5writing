using System.Globalization;

namespace EikenSpelling.Presentation.Logging;

/// <summary>例外をログ用に言い表す。型名・HResult・発生したメソッド名だけ(Message とスタックトレースはパスを含みうるので書かない)。</summary>
public static class ExceptionInfo
{
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var parts = new List<string> { One("type", exception) };
        var inner = exception is AggregateException aggregate ? aggregate.InnerExceptions : (IEnumerable<Exception>)[];
        if (exception is not AggregateException && exception.InnerException is { } single)
        {
            inner = [single];
        }

        parts.AddRange(inner.Select(e => One("inner", e)));
        return string.Join("; ", parts);
    }

    private static string One(string key, Exception e) =>
        $"{key}={e.GetType().FullName} hresult=0x{e.HResult.ToString("X8", CultureInfo.InvariantCulture)} method={e.TargetSite?.Name ?? "-"}";
}
