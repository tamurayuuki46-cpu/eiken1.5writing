using EikenSpelling.Core.Application;
using EikenSpelling.Persistence;

namespace EikenSpelling.Flow.Tests;

public class FlowStartupTests
{
    [Fact]
    public void First_run_without_files_goes_home_without_notices()
    {
        using var env = new FlowEnv();

        var flow = env.Start();

        var home = flow.As<HomeScreen>();
        Assert.Empty(home.Notices);
        Assert.Equal(SelectionEmptyReason.None, home.EmptyReason);
        Assert.Null(env.ProgressJsonText); // 起動だけでは書かない
    }

    public static TheoryData<string?> BadFolders => [null, "", "relative/folder", "."];

    [Theory]
    [MemberData(nameof(BadFolders))]
    public void Folder_that_is_not_fully_qualified_is_fatal_and_creates_nothing(string? folder)
    {
        using var env = new FlowEnv();
        var options = env.Options() with { DataDirectory = folder };
        var before = Directory.GetCurrentDirectory();
        var entries = Directory.GetFileSystemEntries(before).Length;

        var flow = PracticeFlow.Start(options);

        var fatal = flow.As<FatalScreen>();
        Assert.Equal(FatalKind.FolderInvalid, fatal.Kind);
        Assert.False(fatal.CanReload);
        Assert.Equal(entries, Directory.GetFileSystemEntries(before).Length);
    }

    [Fact]
    public void Drive_relative_path_is_fatal_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // C:foo の形は Windows だけの意味を持つ(Linux では相対パスのテストで同じ分岐を通る)
        }

        using var env = new FlowEnv();
        var flow = PracticeFlow.Start(env.Options() with { DataDirectory = "C:eiken-flow-test" });

        Assert.Equal(FatalKind.FolderInvalid, flow.As<FatalScreen>().Kind);
    }

    public static TheoryData<string?> BadWordSets =>
    [
        null,
        "",
        "{ not json",
        """{ "schemaVersion": 99, "words": [] }""",
        Words.Json(new TestWord("apple"), new TestWord("apple")), // 重複は検査のエラー
    ];

    [Theory]
    [MemberData(nameof(BadWordSets))]
    public void Broken_word_set_is_fatal_and_writes_nothing(string? json)
    {
        using var env = new FlowEnv();
        var flow = PracticeFlow.Start(env.Options() with { WordSetJson = json });

        var fatal = flow.As<FatalScreen>();
        Assert.Equal(FatalKind.WordSetInvalid, fatal.Kind);
        Assert.False(fatal.CanReload);
        Assert.Empty(env.Dir.Names());
    }

    [Fact]
    public void Newer_progress_is_fatal_and_file_is_untouched()
    {
        using var env = new FlowEnv();
        env.Dir.Write(ProgressRepository.FileName, """{ "schemaVersion": 99 }""");
        var before = env.Dir.Snapshot();

        var flow = env.Start();

        Assert.Equal(FatalKind.NewerVersion, flow.As<FatalScreen>().Kind);
        Assert.Throws<InvalidOperationException>(flow.Reload);
        Assert.Equal(before, env.Dir.Snapshot());
    }

    [Fact]
    public void Unavailable_progress_is_fatal_and_reload_succeeds_after_it_becomes_readable()
    {
        using var env = new FlowEnv();
        var first = env.Start();
        first.StartPractice(Choices.Auto());
        env.Files.FailReadsOf.Add(ProgressRepository.FileName);
        var before = env.Dir.Snapshot();

        var flow = env.Start();

        var fatal = flow.As<FatalScreen>();
        Assert.Equal(FatalKind.Unavailable, fatal.Kind);
        Assert.True(fatal.CanReload);

        flow.Reload(); // まだ読めない
        Assert.Equal(FatalKind.Unavailable, flow.As<FatalScreen>().Kind);
        Assert.Equal(before, env.Dir.Snapshot());

        env.Files.FailReadsOf.Clear();
        flow.Reload();

        Assert.IsType<ResumePromptScreen>(flow.Screen);
    }

    [Fact]
    public void Reload_rereads_settings_too()
    {
        using var env = new FlowEnv();
        env.Dir.Write(SettingsRepository.FileName, """{ "settingsVersion": 1, "practice": { "retryCount": 0 } }""");
        env.Files.FailReadsOf.Add(SettingsRepository.FileName);
        env.Files.FailReadsOf.Add(ProgressRepository.FileName);
        env.Dir.Write(ProgressRepository.FileName, FlowEnv.EmptyProgressJson);

        var flow = env.Start();
        Assert.Equal(FatalKind.Unavailable, flow.As<FatalScreen>().Kind);

        env.Files.FailReadsOf.Clear();
        flow.Reload();
        var home = flow.As<HomeScreen>();
        Assert.Empty(home.Notices); // 設定も読めたので「初期設定で動いています」は出ない

        flow.StartPractice(Choices.Auto(count: 1));
        flow.AnswerCurrent(false);
        flow.Next();
        Assert.IsType<ResultScreen>(flow.Screen); // retryCount 0 の設定が使われた(再練習なし)
    }

    [Fact]
    public void Reload_does_not_duplicate_notices()
    {
        using var env = new FlowEnv();
        env.Dir.Write(SettingsRepository.FileName, "{ broken");
        env.Dir.Write(ProgressRepository.FileName, FlowEnv.EmptyProgressJson);
        env.Files.FailReadsOf.Add(ProgressRepository.FileName);

        var flow = env.Start();
        env.Files.FailReadsOf.Clear();
        flow.Reload();

        var notices = flow.As<HomeScreen>().Notices;
        Assert.Single(notices);
    }

    [Fact]
    public void Both_progress_files_corrupt_shows_choice_and_quitting_writes_nothing()
    {
        using var env = new FlowEnv();
        env.Dir.Write(ProgressRepository.FileName, "{ broken");
        env.Dir.Write("progress.bak.json", "{ broken");
        var before = env.Dir.Snapshot();

        var flow = env.Start();

        Assert.IsType<BothCorruptScreen>(flow.Screen);
        Assert.Equal(before, env.Dir.Snapshot()); // 「終了」は WPF が閉じるだけ
    }

    [Fact]
    public void Start_fresh_after_both_corrupt_keeps_broken_files_and_goes_home()
    {
        using var env = new FlowEnv { WordsJson = Words.Json("v7", Words.Five) };
        env.Dir.Write(ProgressRepository.FileName, "{ broken");
        env.Dir.Write("progress.bak.json", "{ broken");
        var flow = env.Start();

        flow.StartFresh();

        var home = flow.As<HomeScreen>();
        Assert.Single(home.Notices);
        Assert.Equal("v7", env.Progress().WordSetVersion);
        Assert.Contains(env.Dir.Names(), n => n.Contains(".corrupt-", StringComparison.Ordinal));
    }

    [Fact]
    public void Start_fresh_failure_stays_on_choice_and_can_be_retried()
    {
        using var env = new FlowEnv();
        env.Dir.Write(ProgressRepository.FileName, "{ broken");
        env.Dir.Write("progress.bak.json", "{ broken");
        var flow = env.Start();
        env.Files.FailWrites = true;

        flow.StartFresh();

        Assert.NotNull(flow.As<BothCorruptScreen>().Message);
        env.Files.FailWrites = false;
        flow.StartFresh();
        Assert.IsType<HomeScreen>(flow.Screen);
    }

    [Fact]
    public void Main_corrupt_with_good_backup_recovers_and_notifies()
    {
        using var env = new FlowEnv();
        var first = env.Start();
        first.StartPractice(Choices.Auto());
        first.AnswerCurrent(true);
        first.Next();
        first.AnswerCurrent(true); // 2回目の保存で 1回目の内容が bak に入る
        env.Dir.Write(ProgressRepository.FileName, "{ broken");

        var flow = env.Start();

        var prompt = flow.As<ResumePromptScreen>();
        Assert.NotEmpty(prompt.Notices);
        Assert.Equal(1, prompt.Session.Answered);
    }

    [Theory]
    [InlineData("{ broken")] // 壊れている
    [InlineData("""{ "settingsVersion": 99 }""")] // 新しすぎる
    public void Unusable_settings_notify_and_run_with_defaults_without_writing_settings(string settingsJson)
    {
        using var env = new FlowEnv();
        env.Dir.Write(SettingsRepository.FileName, settingsJson);

        var flow = env.Start();

        var home = flow.As<HomeScreen>();
        Assert.Single(home.Notices);
        flow.StartPractice(Choices.Auto(count: 3));
        Assert.Equal(3, env.Progress().Session!.Questions.Length);
        Assert.Equal(settingsJson, env.Dir.Read(SettingsRepository.FileName));
    }

    [Fact]
    public void Unreadable_settings_notify_and_run_with_defaults()
    {
        using var env = new FlowEnv();
        env.Dir.Write(SettingsRepository.FileName, "{}");
        env.Files.FailReadsOf.Add(SettingsRepository.FileName);

        var flow = env.Start();

        Assert.Single(flow.As<HomeScreen>().Notices);
    }

    [Fact]
    public void Notices_contain_neither_folder_path_nor_wrong_input()
    {
        using var env = new FlowEnv();
        var first = env.Start();
        first.StartPractice(Choices.Auto());
        first.Answer("SECRET-INPUT");
        first.Next();
        first.AnswerCurrent(true);
        env.Dir.Write(ProgressRepository.FileName, "{ broken");
        env.Dir.Write(SettingsRepository.FileName, "{ broken");

        var flow = env.Start();

        var notices = flow.Screen.Notices;
        Assert.NotEmpty(notices);
        Assert.All(notices, n =>
        {
            Assert.DoesNotContain(env.Dir.Path, n, StringComparison.Ordinal);
            Assert.DoesNotContain("SECRET", n, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Notices_are_shown_only_on_the_first_screen()
    {
        using var env = new FlowEnv();
        env.Dir.Write(SettingsRepository.FileName, "{ broken");
        var flow = env.Start();
        Assert.NotEmpty(flow.Screen.Notices);

        flow.StartPractice(Choices.Auto(count: 1));
        Assert.Empty(flow.Screen.Notices);
        flow.AnswerAll(_ => true);
        flow.GoHome();

        Assert.Empty(flow.As<HomeScreen>().Notices);
    }
}
