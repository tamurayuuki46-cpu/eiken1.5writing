namespace EikenSpelling.Presentation;

/// <summary>
/// 起動引数(design 4.1、決定 2)。`--data-dir &lt;値&gt;` で保存先を差し替える。値の検査は Flow(完全な絶対パスでなければ FolderInvalid)。
/// 値なし・2回は空文字にして Flow に止めさせる。知らない引数は無視し、あったことだけを返す(中身はログに書かない)。
/// </summary>
public sealed record StartupArguments(string DataDirectory, bool HadUnknown)
{
    public const string DataDirOption = "--data-dir";

    public static StartupArguments Parse(IReadOnlyList<string> args, string? defaultDirectory)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? dataDir = null;
        var seen = 0;
        var hadUnknown = false;
        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], DataDirOption, StringComparison.Ordinal))
            {
                seen++;
                dataDir = i + 1 < args.Count ? args[++i] : null;
            }
            else
            {
                hadUnknown = true;
            }
        }

        var directory = seen switch
        {
            0 => defaultDirectory ?? "",
            1 => dataDir ?? "",
            _ => "",
        };
        return new StartupArguments(directory, hadUnknown);
    }
}
