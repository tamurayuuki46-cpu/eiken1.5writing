using System.Globalization;
using System.Text;

using EikenSpelling.Persistence.Tests;
using EikenSpelling.Presentation.Logging;

namespace EikenSpelling.Presentation.Tests;

public class StartupArgumentsTests
{
    private const string Default = @"C:\Users\x\AppData\Roaming\EikenSpelling";

    [Fact]
    public void No_arguments_use_the_default_directory() =>
        Assert.Equal(new StartupArguments(Default, HadUnknown: false), StartupArguments.Parse([], Default));

    [Fact]
    public void Data_dir_value_is_used_as_is_and_checked_later_by_flow()
    {
        Assert.Equal(@"D:\temp\eiken", StartupArguments.Parse(["--data-dir", @"D:\temp\eiken"], Default).DataDirectory);
        Assert.Equal("relative", StartupArguments.Parse(["--data-dir", "relative"], Default).DataDirectory);
    }

    [Theory]
    [InlineData("--data-dir")]
    [InlineData("--data-dir|D:\\a|--data-dir|D:\\b")]
    public void Missing_or_repeated_data_dir_becomes_empty_so_flow_stops(string args) =>
        Assert.Equal("", StartupArguments.Parse(args.Split('|'), Default).DataDirectory);

    [Fact]
    public void Unknown_arguments_are_ignored_and_reported()
    {
        var parsed = StartupArguments.Parse(["--secret-path", @"C:\Users\x"], Default);

        Assert.Equal(Default, parsed.DataDirectory);
        Assert.True(parsed.HadUnknown);
    }

    [Fact]
    public void Missing_default_directory_becomes_empty() =>
        Assert.Equal("", StartupArguments.Parse([], null).DataDirectory);
}

public class FileLogTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 10, 0, 0, TimeSpan.FromHours(9));

    [Fact]
    public void A_line_has_keys_and_values_in_a_file_named_by_the_japan_date()
    {
        using var dir = new TempDir();
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 10, 31, 15, 30, 0, TimeSpan.Zero)); // 日本時間 11/1 0:30
        var log = new FileLog(dir.Path, time);
        var session = new Guid(1, 0, 0, new byte[8]);

        log.Write(LogLevel.Error, "FeedbackScreen", "保存に失敗 \"引用\"\n改行", session);

        var line = Assert.Single(File.ReadAllLines(dir.File("app-20261101.log"), Encoding.UTF8));
        Assert.StartsWith("time=2026-11-01T00:30:00.000+09:00 level=error op=FeedbackScreen sessionId=" + session, line, StringComparison.Ordinal);
        Assert.Contains("reason=\"保存に失敗 \\\"引用\\\" 改行\"", line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative\\logs")]
    public void Not_fully_qualified_folder_writes_and_deletes_nothing(string folder)
    {
        var log = new FileLog(folder, new FixedTimeProvider(T0));

        log.Write(LogLevel.Error, "op");
        log.DeleteOld();

        Assert.False(Directory.Exists(Path.Combine(Environment.CurrentDirectory, "relative")));
    }

    [Fact]
    public void Unwritable_folder_does_not_throw()
    {
        using var dir = new TempDir();
        dir.Write("blocker", "x"); // フォルダの場所にファイルがある
        var log = new FileLog(dir.File("blocker"), new FixedTimeProvider(T0));

        log.Write(LogLevel.Error, "op", "reason");
        log.Write(LogLevel.Warn, "op2");
        log.DeleteOld();
    }

    [Fact]
    public void Old_logs_beyond_thirty_days_are_deleted_and_nothing_else_is_touched()
    {
        using var dir = new TempDir();
        foreach (var name in new[] { "app-20261001.log", "app-20261002.log", "app-20261003.log", "app-foo.log", "app-20200101.logx", "other.txt", "app-20261399.log" })
        {
            dir.Write(name, "x");
        }

        Directory.CreateDirectory(dir.File("app-20200101.log")); // 名前は合うがフォルダ
        File.WriteAllText(Path.Combine(dir.File("app-20200101.log"), "app-20200101.log"), "x");

        new FileLog(dir.Path, new FixedTimeProvider(T0)).DeleteOld(); // 今日は 11/1

        Assert.Equal(["app-20200101.logx", "app-20261002.log", "app-20261003.log", "app-20261399.log", "app-foo.log", "other.txt"], dir.Names());
        Assert.True(File.Exists(Path.Combine(dir.File("app-20200101.log"), "app-20200101.log")));
    }

    [Fact]
    public void Exception_description_has_type_and_hresult_but_no_message_or_path()
    {
        Exception ex;
        try
        {
            throw new AggregateException(new IOException(@"C:\Users\secret\progress.json を開けません"));
        }
        catch (AggregateException caught)
        {
            ex = caught;
        }

        var text = ExceptionInfo.Describe(ex);

        Assert.Contains("type=System.AggregateException", text, StringComparison.Ordinal);
        Assert.Contains("inner=System.IO.IOException", text, StringComparison.Ordinal);
        Assert.Contains("hresult=0x" + new IOException().HResult.ToString("X8", CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
        Assert.Contains("method=Exception_description_has_type_and_hresult_but_no_message_or_path", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
    }
}
