using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

namespace EikenSpelling.Persistence.Tests;

/// <summary>progress.json の持ち主(design.md 4章「Repository のロック」ほか)。</summary>
public class ProgressRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.FromHours(9));
    private static readonly LearningSettings Settings = DefaultSettings.Create();

    private static ProgressRepository Repo(TempDir dir, IFileOperations? files = null) =>
        new(dir.Path, new FixedTimeProvider(Now), files, TimeSpan.Zero);

    private static ProgressDocument Sample()
    {
        var progress = LearningProgress.Empty;
        var answer = new AnswerEvent(new AnswerId(Guid.NewGuid(), 0, AttemptType.Normal), "tax", LearningMode.Japanese, false, "taks", Now.AddDays(-1));
        progress = AnswerRecorder.Apply(progress, answer, Settings).Progress;
        return new ProgressDocument { WordSetVersion = "ws-1", SourceVersion = "src-1", Progress = progress };
    }

    [Fact]
    public void Save_then_load_with_new_instance_restores_document()
    {
        using var dir = new TempDir();
        var repo = Repo(dir);
        Assert.Equal(LoadStatus.FirstRun, repo.Load(Settings).Status);

        Assert.True(repo.Save(Sample()).Succeeded);
        var load = Repo(dir).Load(Settings);

        Assert.Equal(LoadStatus.Loaded, load.Status);
        Assert.Equal("ws-1", load.Document!.WordSetVersion);
        Assert.Equal(LearningState.Weak, load.Document.Progress.Word("tax")!.State);
        Assert.Single(load.Document.Progress.AppliedAnswerIds);
    }

    [Fact]
    public void First_run_returns_empty_document()
    {
        using var dir = new TempDir();

        var load = Repo(dir).Load(Settings);

        Assert.Equal(LoadStatus.FirstRun, load.Status);
        Assert.Empty(load.Document!.Progress.Words);
    }

    [Fact]
    public void Missing_directory_is_created()
    {
        using var dir = new TempDir();
        var nested = Path.Combine(dir.Path, "EikenSpelling");
        var repo = new ProgressRepository(nested, new FixedTimeProvider(Now), retryDelay: TimeSpan.Zero);

        Assert.Equal(LoadStatus.FirstRun, repo.Load(Settings).Status);
        Assert.True(repo.Save(Sample()).Succeeded);
        Assert.True(File.Exists(Path.Combine(nested, "progress.json")));
    }

    [Fact]
    public void Save_before_load_is_rejected()
    {
        using var dir = new TempDir();

        Assert.False(Repo(dir).Save(Sample()).Succeeded);
        Assert.Empty(dir.Names());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("{ \"schemaVersion\": 1, \"progress\": { \"words\": { \"fax\": { \"wordId\": \"tax\" } } } }")] // JSON は正しいが検査で重大
    public void Corrupt_or_critical_main_is_recovered_from_backup(string mainContent)
    {
        using var dir = new TempDir();
        var repo = Repo(dir);
        repo.Load(Settings);
        repo.Save(Sample());
        repo.Save(Sample() with { WordSetVersion = "ws-2" }); // bak = ws-1
        dir.Write("progress.json", mainContent);

        var load = Repo(dir).Load(Settings);

        Assert.Equal(LoadStatus.RecoveredFromBackup, load.Status);
        Assert.Equal("ws-1", load.Document!.WordSetVersion);
    }

    [Fact]
    public void Critical_and_warning_together_is_recovered_from_backup()
    {
        using var dir = new TempDir();
        var repo = Repo(dir);
        repo.Load(Settings);
        repo.Save(Sample());
        repo.Save(Sample() with { WordSetVersion = "ws-2" }); // bak = ws-1
        var bad = Sample() with { WordSetVersion = "ws-2" };
        var w = bad.Progress.Word("tax")!;
        bad = bad with
        {
            Progress = bad.Progress with
            {
                Words = bad.Progress.Words.SetItem("tax", w with { LastNormalAskedAt = Now.AddDays(10), ReviewStage = 1 }), // 警告 + 重大
            },
        };
        dir.Write("progress.json", ProgressJson.Serialize(bad));

        var load = Repo(dir).Load(Settings);

        Assert.Equal(LoadStatus.RecoveredFromBackup, load.Status);
        Assert.Equal("ws-1", load.Document!.WordSetVersion);
    }

    [Fact]
    public void Warnings_are_reported_with_loaded()
    {
        using var dir = new TempDir();
        var future = Sample();
        var w = future.Progress.Word("tax")!;
        future = future with { Progress = future.Progress with { Words = future.Progress.Words.SetItem("tax", w with { LastNormalAskedAt = Now.AddDays(10) }) } };
        dir.Write("progress.json", ProgressJson.Serialize(future));

        var load = Repo(dir).Load(Settings);

        Assert.Equal(LoadStatus.Loaded, load.Status);
        Assert.NotEmpty(load.Warnings);
    }

    [Fact]
    public void Newer_version_blocks_save()
    {
        using var dir = new TempDir();
        dir.Write("progress.json", """{ "schemaVersion": 99, "progress": "new-shape" }""");
        var repo = Repo(dir);

        Assert.Equal(LoadStatus.NewerVersion, repo.Load(Settings).Status);
        Assert.False(repo.Save(Sample()).Succeeded);
        Assert.False(repo.StartFresh("ws", "src").Succeeded);
        Assert.Equal("""{ "schemaVersion": 99, "progress": "new-shape" }""", dir.Read("progress.json"));
    }

    [Fact]
    public void Unavailable_blocks_save()
    {
        using var dir = new TempDir();
        dir.Write("progress.json", ProgressJson.Serialize(Sample()));
        var faulty = new FaultyFileOperations();
        faulty.Faults["ReadAllText"] = (1, int.MaxValue);
        var repo = Repo(dir, faulty);
        var before = dir.Snapshot();

        Assert.Equal(LoadStatus.Unavailable, repo.Load(Settings).Status);
        Assert.False(repo.Save(Sample()).Succeeded);
        Assert.Equal(before, dir.Snapshot());
    }

    [Fact]
    public void Both_corrupt_blocks_save_until_start_fresh()
    {
        using var dir = new TempDir();
        dir.Write("progress.json", "broken-main");
        dir.Write("progress.bak.json", "broken-bak");
        var repo = Repo(dir);

        Assert.Equal(LoadStatus.BothCorrupt, repo.Load(Settings).Status);
        Assert.False(repo.Save(Sample()).Succeeded);

        Assert.True(repo.StartFresh("ws-new", "src-new").Succeeded);
        Assert.True(repo.Save(Sample()).Succeeded);

        var kept = dir.Names().Where(n => n.Contains(".corrupt-", StringComparison.Ordinal)).Select(dir.Read).Order().ToList();
        Assert.Equal(["broken-bak", "broken-main"], kept);
        Assert.Equal(LoadStatus.Loaded, Repo(dir).Load(Settings).Status);
    }

    [Fact]
    public void Start_fresh_creates_empty_record_with_versions()
    {
        using var dir = new TempDir();
        dir.Write("progress.json", "broken-main");
        var repo = Repo(dir);
        repo.Load(Settings);

        Assert.True(repo.StartFresh("ws-new", "src-new").Succeeded);

        var load = Repo(dir).Load(Settings);
        Assert.Equal(LoadStatus.Loaded, load.Status);
        Assert.Equal("ws-new", load.Document!.WordSetVersion);
        Assert.Empty(load.Document.Progress.Words);
    }

    [Fact]
    public void Start_fresh_is_rejected_unless_both_corrupt()
    {
        using var dir = new TempDir();
        var repo = Repo(dir);
        repo.Load(Settings);
        repo.Save(Sample());

        Assert.False(repo.StartFresh("ws", "src").Succeeded);
        Assert.Equal("ws-1", Repo(dir).Load(Settings).Document!.WordSetVersion);
    }

    [Fact]
    public void Relative_directory_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new ProgressRepository("relative"));
    }
}

/// <summary>settings.json の持ち主(design.md 4章「settings.json の読み方」)。</summary>
public class SettingsRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.FromHours(9));

    private static SettingsRepository Repo(TempDir dir, IFileOperations? files = null) =>
        new(dir.Path, new FixedTimeProvider(Now), files, TimeSpan.Zero);

    private static LearningSettings Custom() =>
        DefaultSettings.Create() with { Practice = DefaultSettings.Create().Practice with { QuestionCount = 20 } };

    [Fact]
    public void First_run_uses_defaults_and_writes_nothing()
    {
        using var dir = new TempDir();

        var load = Repo(dir).Load();

        Assert.Equal(LoadStatus.FirstRun, load.Status);
        Assert.Equal(DefaultSettings.Create().Practice.QuestionCount, load.Settings.Practice.QuestionCount);
        Assert.Empty(dir.Names());
    }

    [Fact]
    public void Saved_settings_are_restored()
    {
        using var dir = new TempDir();
        var repo = Repo(dir);
        repo.Load();

        Assert.Equal(SettingsSaveStatus.Saved, repo.Save(Custom()).Status);

        var load = Repo(dir).Load();
        Assert.Equal(LoadStatus.Loaded, load.Status);
        Assert.Equal(20, load.Settings.Practice.QuestionCount);
    }

    [Fact]
    public void Invalid_settings_are_not_saved()
    {
        using var dir = new TempDir();
        var repo = Repo(dir);
        repo.Load();
        repo.Save(Custom());
        var invalid = DefaultSettings.Create() with { Practice = DefaultSettings.Create().Practice with { QuestionCount = 0 } };

        var r = repo.Save(invalid);

        Assert.Equal(SettingsSaveStatus.Invalid, r.Status);
        Assert.Contains(r.Issues, i => i.Path == "practice.questionCount");
        Assert.Equal(20, Repo(dir).Load().Settings.Practice.QuestionCount);
    }

    [Fact]
    public void Corrupt_main_is_recovered_from_backup()
    {
        using var dir = new TempDir();
        var repo = Repo(dir);
        repo.Load();
        repo.Save(Custom());
        repo.Save(DefaultSettings.Create()); // bak = 20問
        dir.Write("settings.json", "{ broken");

        var load = Repo(dir).Load();

        Assert.Equal(LoadStatus.RecoveredFromBackup, load.Status);
        Assert.Equal(20, load.Settings.Practice.QuestionCount);
    }

    [Fact]
    public void Readable_but_invalid_file_falls_back_to_defaults_without_writing()
    {
        using var dir = new TempDir();
        dir.Write("settings.json", """{ "practice": { "questionCount": 0 } }""");
        var before = dir.Snapshot();

        var load = Repo(dir).Load();

        Assert.Equal(LoadStatus.BothCorrupt, load.Status);
        Assert.Equal(DefaultSettings.Create().Practice.QuestionCount, load.Settings.Practice.QuestionCount);
        Assert.NotNull(load.Reason);
        Assert.Equal(before, dir.Snapshot());
    }

    [Fact]
    public void Save_after_both_corrupt_keeps_broken_files()
    {
        using var dir = new TempDir();
        dir.Write("settings.json", "{ broken-main");
        dir.Write("settings.bak.json", "{ broken-bak");
        var repo = Repo(dir);
        Assert.Equal(LoadStatus.BothCorrupt, repo.Load().Status);

        Assert.Equal(SettingsSaveStatus.Saved, repo.Save(Custom()).Status);

        var kept = dir.Names().Where(n => n.Contains(".corrupt-", StringComparison.Ordinal)).Select(dir.Read).Order().ToList();
        Assert.Equal(["{ broken-bak", "{ broken-main"], kept);
        Assert.Equal(20, Repo(dir).Load().Settings.Practice.QuestionCount);
    }

    [Fact]
    public void Newer_settings_use_defaults_and_block_save()
    {
        using var dir = new TempDir();
        dir.Write("settings.json", """{ "settingsVersion": 99 }""");
        var repo = Repo(dir);

        var load = repo.Load();

        Assert.Equal(LoadStatus.NewerVersion, load.Status);
        Assert.Equal(LearningSettings.CurrentVersion, load.Settings.SettingsVersion);
        Assert.Equal(SettingsSaveStatus.Failed, repo.Save(Custom()).Status);
        Assert.Equal("""{ "settingsVersion": 99 }""", dir.Read("settings.json"));
    }

    [Fact]
    public void Newer_settings_with_changed_shape_is_not_recovered()
    {
        using var dir = new TempDir();
        var repo = Repo(dir);
        repo.Load();
        repo.Save(Custom());
        repo.Save(Custom()); // bak も正常
        dir.Write("settings.json", """{ "settingsVersion": 2, "practice": "new-shape" }""");
        var before = dir.Snapshot();

        var load = Repo(dir).Load();

        Assert.Equal(LoadStatus.NewerVersion, load.Status);
        Assert.Equal(before, dir.Snapshot());
    }

    [Fact]
    public void Save_before_load_is_rejected()
    {
        using var dir = new TempDir();

        Assert.Equal(SettingsSaveStatus.Failed, Repo(dir).Save(Custom()).Status);
        Assert.Empty(dir.Names());
    }

    [Fact]
    public void Settings_failure_does_not_touch_progress()
    {
        using var dir = new TempDir();
        dir.Write("progress.json", "progress-content");
        dir.Write("settings.json", "{ broken");

        var repo = Repo(dir);
        repo.Load();
        repo.Save(Custom());

        Assert.Equal("progress-content", dir.Read("progress.json"));
    }
}
