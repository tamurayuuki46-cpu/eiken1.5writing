using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using EikenSpelling.Core.Domain;

namespace EikenSpelling.Presentation.Logging;

/// <summary>
/// 手書きのファイルログ(design 4.6)。`app-yyyyMMdd.log`(日本時間の日付)に1行1件、キーと値で追記する。
/// ログの失敗でアプリを止めない(例外を外へ出さず、1回失敗したら以後は書かない)。
/// フォルダが完全な絶対パスでなければ、書き込みも削除もしない。
/// </summary>
public sealed partial class FileLog : IAppLog
{
    /// <summary>これより古いログを起動時に消す(決定 5)。学習ルールではないのでここに置く。</summary>
    public static readonly int KeepDays = 30;

    private readonly string? _directory;
    private readonly TimeProvider _time;
    private bool _broken;

    public FileLog(string? directory, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _directory = !string.IsNullOrEmpty(directory) && Path.IsPathFullyQualified(directory) ? directory : null;
        _time = time;
    }

    public void Write(LogLevel level, string op, string? reason = null, Guid? sessionId = null)
    {
        if (_directory is null || _broken)
        {
            return;
        }

        var now = JstClock.Now(_time);
        var line = new StringBuilder()
            .Append("time=").Append(now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture))
            .Append(" level=").Append(level.ToString().ToLowerInvariant())
            .Append(" op=").Append(op);
        if (sessionId is { } id)
        {
            line.Append(" sessionId=").Append(id);
        }

        if (reason is not null)
        {
            line.Append(" reason=\"").Append(Escape(reason)).Append('"');
        }

        try
        {
            Directory.CreateDirectory(_directory);
            File.AppendAllText(Path.Combine(_directory, FileName(DateOnly.FromDateTime(now.DateTime))), line.Append('\n').ToString(), new UTF8Encoding(false));
        }
#pragma warning disable CA1031 // ログの失敗でアプリを止めない(例外の受け口の中からも呼ばれる)ため、すべて捕まえる
        catch (Exception)
#pragma warning restore CA1031
        {
            _broken = true;
        }
    }

    /// <summary>
    /// 30 日より古いログを消す。名前が `app-yyyyMMdd.log` に完全一致するファイルだけ、古さは名前の日付(日本時間)で決める。
    /// 一番上のフォルダだけを見て、フォルダは消さない。1件の失敗でも続ける。
    /// </summary>
    public void DeleteOld()
    {
        if (_directory is null || !Directory.Exists(_directory))
        {
            return;
        }

        var oldest = JstClock.Today(_time).AddDays(-KeepDays);
        string[] files;
        try
        {
            files = Directory.GetFiles(_directory, "*", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var file in files)
        {
            var match = LogName().Match(Path.GetFileName(file));
            if (!match.Success
                || !DateOnly.TryParseExact(match.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || date >= oldest)
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 消せないファイルは残して続ける
            }
        }
    }

    private static string FileName(DateOnly date) => $"app-{date.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log";

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    [GeneratedRegex(@"^app-(\d{8})\.log$", RegexOptions.CultureInvariant)]
    private static partial Regex LogName();
}
