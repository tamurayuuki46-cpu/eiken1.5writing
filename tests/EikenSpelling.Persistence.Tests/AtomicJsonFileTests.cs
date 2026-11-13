using System.Text;

namespace EikenSpelling.Persistence.Tests;

/// <summary>
/// 保存・読込・復旧の手順(design.md 4章)。中身は単純な文字列にして手順だけを確かめる:
/// "ok:..." は正常、"newer:..." は新しすぎる版、それ以外は壊れている。
/// </summary>
public class AtomicJsonFileTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.FromHours(9));

    private static FileRead<string> Read(string text) =>
        text.StartsWith("boom", StringComparison.Ordinal) ? throw new InvalidOperationException("読み方の不具合(テスト)")
        : text.StartsWith("ok:", StringComparison.Ordinal) ? FileRead<string>.Ok(text, text.Contains("warn") ? ["警告"] : null)
        : text.StartsWith("newer:", StringComparison.Ordinal) ? FileRead<string>.Newer("新しい版")
        : FileRead<string>.Corrupt("壊れている");

    private static AtomicJsonFile<string> File(TempDir dir, IFileOperations? files = null, FixedTimeProvider? time = null) =>
        new(dir.Path, "progress.json", Read, files, time ?? new FixedTimeProvider(Now), TimeSpan.Zero);

    // ---- 保存 ----

    [Fact]
    public void First_save_creates_main_without_backup()
    {
        using var dir = new TempDir();

        Assert.True(File(dir).Save("ok:1").Succeeded);

        Assert.Equal("ok:1", dir.Read("progress.json"));
        Assert.Null(dir.Read("progress.bak.json"));
        Assert.Null(dir.Read("progress.json.tmp"));
    }

    [Fact]
    public void Second_save_moves_previous_content_to_backup()
    {
        using var dir = new TempDir();
        var f = File(dir);
        f.Save("ok:1");

        Assert.True(f.Save("ok:2").Succeeded);

        Assert.Equal("ok:2", dir.Read("progress.json"));
        Assert.Equal("ok:1", dir.Read("progress.bak.json"));
    }

    [Fact]
    public void Saved_file_has_no_bom()
    {
        using var dir = new TempDir();

        File(dir).Save("ok:1");

        var bytes = System.IO.File.ReadAllBytes(dir.File("progress.json"));
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }

    [Fact]
    public void Content_failing_read_back_is_not_saved()
    {
        using var dir = new TempDir();
        dir.Write("progress.json", "ok:1");
        dir.Write("progress.bak.json", "ok:0");

        var r = File(dir).Save("broken");

        Assert.False(r.Succeeded);
        Assert.Equal("ok:1", dir.Read("progress.json"));
        Assert.Equal("ok:0", dir.Read("progress.bak.json"));
        Assert.Null(dir.Read("progress.json.tmp"));
    }

    [Theory]
    [InlineData("WriteAndFlush")]
    [InlineData("ReadAllText")]
    [InlineData("Replace")]
    public void Failure_at_any_step_keeps_main_and_backup(string op)
    {
        using var dir = new TempDir();
        dir.Write("progress.json", "ok:1");
        dir.Write("progress.bak.json", "ok:0");
        var faulty = new FaultyFileOperations();
        faulty.Faults[op] = (1, int.MaxValue);

        var r = File(dir, faulty).Save("ok:2");

        Assert.False(r.Succeeded);
        Assert.NotNull(r.Reason);
        Assert.DoesNotContain(dir.Path, r.Reason);
        Assert.Equal("ok:1", dir.Read("progress.json"));
        Assert.Equal("ok:0", dir.Read("progress.bak.json"));
    }

    [Fact]
    public void Failure_of_first_move_creates_no_main()
    {
        using var dir = new TempDir();
        var faulty = new FaultyFileOperations();
        faulty.Faults["Move"] = (1, int.MaxValue);

        Assert.False(File(dir, faulty).Save("ok:1").Succeeded);

        Assert.Null(dir.Read("progress.json"));
    }

    [Fact]
    public void Transient_failure_is_retried()
    {
        using var dir = new TempDir();
        dir.Write("progress.json", "ok:1");
        var faulty = new FaultyFileOperations();
        faulty.Faults["Replace"] = (1, AtomicJsonFile<string>.MaxAttempts - 1);

        Assert.True(File(dir, faulty).Save("ok:2").Succeeded);

        Assert.Equal("ok:2", dir.Read("progress.json"));
        Assert.Equal(AtomicJsonFile<string>.MaxAttempts, faulty.Calls("Replace"));
    }

    [Fact]
    public void Old_temp_file_is_overwritten()
    {
        using var dir = new TempDir();
        dir.Write("progress.json.tmp", "ok:stale");

        Assert.True(File(dir).Save("ok:1").Succeeded);

        Assert.Equal("ok:1", dir.Read("progress.json"));
    }

    [Fact]
    public void Replace_stopping_halfway_is_recovered_on_next_load()
    {
        using var dir = new TempDir();
        var f = File(dir);
        f.Save("ok:1");
        var faulty = new FaultyFileOperations { ReplaceStopsHalfway = true };

        var failed = File(dir, faulty).Save("ok:2");
        Assert.False(failed.Succeeded);
        Assert.DoesNotContain("前回の内容は残っています", failed.Reason);
        Assert.Equal(1, faulty.Calls("Replace")); // 主が無くなったら再試行しない
        Assert.Null(dir.Read("progress.json")); // 主が無く tmp が残る途中状態

        var load = File(dir).Load();

        Assert.Equal(LoadStatus.RecoveredFromBackup, load.Status);
        Assert.Equal("ok:1", load.Value);
        Assert.Contains(dir.Names(), n => n.StartsWith("progress.json.tmp.orphan-", StringComparison.Ordinal)); // 最新の内容は残す
    }

    [Fact]
    public void Save_after_halfway_replace_succeeds_via_move()
    {
        using var dir = new TempDir();
        File(dir).Save("ok:1");
        File(dir, new FaultyFileOperations { ReplaceStopsHalfway = true }).Save("ok:2");

        Assert.True(File(dir).Save("ok:2").Succeeded);

        Assert.Equal("ok:2", dir.Read("progress.json"));
        Assert.Equal("ok:1", dir.Read("progress.bak.json"));
    }

    // ---- 読込 ----

    [Fact]
    public void Valid_main_is_loaded_and_backup_is_not_touched()
    {
        using var dir = new TempDir();
        dir.Write("progress.json", "ok:1");
        dir.Write("progress.bak.json", "broken");

        var load = File(dir).Load();

        Assert.Equal(LoadStatus.Loaded, load.Status);
        Assert.Equal("ok:1", load.Value);
        Assert.Equal("broken", dir.Read("progress.bak.json"));
    }

    [Fact]
    public void Warnings_are_returned_with_loaded()
    {
        using var dir = new TempDir();
        dir.Write("progress.json", "ok:warn");

        var load = File(dir).Load();

        Assert.Equal(LoadStatus.Loaded, load.Status);
        Assert.Single(load.Warnings);
    }

    [Fact]
    public void Bom_prefixed_valid_file_is_loaded()
    {
        using var dir = new TempDir();
        System.IO.File.WriteAllText(dir.File("progress.json"), "ok:1", new UTF8Encoding(true));

        Assert.Equal("ok:1", File(dir).Load().Value);
    }

    [Fact]
    public void No_files_is_first_run()
    {
        using var dir = new TempDir();

        Assert.Equal(LoadStatus.FirstRun, File(dir).Load().Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Corrupt_or_missing_main_is_recovered_from_backup(bool mainExists)
    {
        using var dir = new TempDir();
        if (mainExists)
        {
            dir.Write("progress.json", "broken-main");
        }

        dir.Write("progress.bak.json", "ok:0");

        var load = File(dir).Load();

        Assert.Equal(LoadStatus.RecoveredFromBackup, load.Status);
        Assert.Equal("ok:0", load.Value);
        Assert.NotNull(load.Reason);
        Assert.Equal("ok:0", dir.Read("progress.json")); // 主ファイルを作り直す
        Assert.Equal("ok:0", dir.Read("progress.bak.json"));
        var corrupt = dir.Names().Where(n => n.StartsWith("progress.json.corrupt-", StringComparison.Ordinal)).ToList();
        Assert.Equal(mainExists ? 1 : 0, corrupt.Count);
        if (mainExists)
        {
            Assert.Equal("broken-main", dir.Read(corrupt[0])); // 壊れたファイルは消さずに残す
        }
    }

    [Fact]
    public void Save_after_recovery_keeps_valid_backup()
    {
        using var dir = new TempDir();
        dir.Write("progress.json", "broken-main");
        dir.Write("progress.bak.json", "ok:0");
        var f = File(dir);
        f.Load();

        Assert.True(f.Save("ok:1").Succeeded);

        Assert.Equal("ok:1", dir.Read("progress.json"));
        Assert.Equal("ok:0", dir.Read("progress.bak.json")); // 壊れたファイルに置き換わらない
    }

    [Fact]
    public void Too_large_main_is_treated_as_corrupt()
    {
        using var dir = new TempDir();
        dir.Write("progress.json", "ok:1");
        dir.Write("progress.bak.json", "ok:0");
        var faulty = new FaultyFileOperations();
        faulty.LengthOverrides["progress.json"] = AtomicJsonFile<string>.MaxFileBytes + 1;

        var load = File(dir, faulty).Load();

        Assert.Equal(LoadStatus.RecoveredFromBackup, load.Status);
        Assert.Equal("ok:0", load.Value);
    }

    [Fact]
    public void Exactly_max_bytes_is_loaded()
    {
        using var dir = new TempDir();
        dir.Write("progress.json", "ok:1");
        var faulty = new FaultyFileOperations();
        faulty.LengthOverrides["progress.json"] = AtomicJsonFile<string>.MaxFileBytes;

        Assert.Equal(LoadStatus.Loaded, File(dir, faulty).Load().Status);
    }

    [Theory]
    [InlineData(false)] // 主が無い: 最初の読み込みが bak
    [InlineData(true)]  // 主が壊れている: 2回目の読み込みから bak
    public void Locked_backup_is_unavailable_and_changes_nothing(bool mainExists)
    {
        using var dir = new TempDir();
        if (mainExists)
        {
            dir.Write("progress.json", "broken-main");
        }

        dir.Write("progress.bak.json", "ok:0");
        var faulty = new FaultyFileOperations();
        faulty.Faults["ReadAllText"] = (mainExists ? 2 : 1, int.MaxValue);
        var before = dir.Snapshot();

        Assert.Equal(LoadStatus.Unavailable, File(dir, faulty).Load().Status);
        Assert.Equal(before, dir.Snapshot());
    }

    [Fact]
    public void Read_back_throwing_is_reported_as_failure()
    {
        using var dir = new TempDir();
        dir.Write("progress.json", "ok:1");

        var r = File(dir).Save("boom");

        Assert.False(r.Succeeded);
        Assert.Equal("ok:1", dir.Read("progress.json"));
        Assert.Null(dir.Read("progress.json.tmp"));
    }

    [Fact]
    public void Evacuate_keeps_leftover_temp_as_orphan()
    {
        using var dir = new TempDir();
        dir.Write("progress.json", "broken-main");
        dir.Write("progress.bak.json", "broken-bak");
        dir.Write("progress.json.tmp", "ok:latest");

        Assert.True(File(dir).EvacuateAndSave("ok:new").Succeeded);

        Assert.Contains("ok:latest", dir.Snapshot().Values);
        Assert.Equal("ok:new", dir.Read("progress.json"));
    }

    [Fact]
    public void Evacuate_validates_content_before_moving_anything()
    {
        using var dir = new TempDir();
        dir.Write("progress.json", "broken-main");
        dir.Write("progress.bak.json", "broken-bak");
        var before = dir.Snapshot();

        Assert.False(File(dir).EvacuateAndSave("broken-new").Succeeded);

        Assert.Equal(before, dir.Snapshot());
    }

    [Fact]
    public void Recover_does_not_overwrite_temp_when_orphan_move_fails()
    {
        using var dir = new TempDir();
        dir.Write("progress.json", "broken-main");
        dir.Write("progress.bak.json", "ok:0");
        dir.Write("progress.json.tmp", "ok:latest");
        var faulty = new FaultyFileOperations();
        faulty.Faults["Move"] = (1, 1); // orphan 名への移動だけ失敗

        Assert.Equal(LoadStatus.Unavailable, File(dir, faulty).Load().Status);

        Assert.Equal("ok:latest", dir.Read("progress.json.tmp"));
        Assert.Equal("ok:0", dir.Read("progress.bak.json"));
    }

    [Theory]
    [InlineData("broken", "broken")]
    [InlineData(null, "broken")]
    [InlineData("broken", null)]
    public void Both_corrupt_changes_nothing(string? main, string? bak)
    {
        using var dir = new TempDir();
        if (main is not null)
        {
            dir.Write("progress.json", main);
        }

        if (bak is not null)
        {
            dir.Write("progress.bak.json", bak);
        }

        dir.Write("progress.json.tmp", "ok:latest");
        var before = dir.Snapshot();

        var load = File(dir).Load();

        Assert.Equal(LoadStatus.BothCorrupt, load.Status);
        Assert.Null(load.Value);
        Assert.Equal(before, dir.Snapshot());
    }

    [Theory]
    [InlineData("newer:1", "ok:0")]
    [InlineData("broken", "newer:0")]
    [InlineData(null, "newer:0")]
    public void Newer_version_is_not_overwritten_or_recovered(string? main, string bak)
    {
        using var dir = new TempDir();
        if (main is not null)
        {
            dir.Write("progress.json", main);
        }

        dir.Write("progress.bak.json", bak);
        var before = dir.Snapshot();

        var load = File(dir).Load();

        Assert.Equal(LoadStatus.NewerVersion, load.Status);
        Assert.Equal(before, dir.Snapshot());
    }

    [Fact]
    public void Locked_main_is_unavailable_not_corrupt()
    {
        using var dir = new TempDir();
        dir.Write("progress.json", "ok:1");
        dir.Write("progress.bak.json", "ok:0");
        var faulty = new FaultyFileOperations();
        faulty.Faults["ReadAllText"] = (1, int.MaxValue);
        var before = dir.Snapshot();

        var load = File(dir, faulty).Load();

        Assert.Equal(LoadStatus.Unavailable, load.Status);
        Assert.Equal(AtomicJsonFile<string>.MaxAttempts, faulty.Calls("ReadAllText"));
        Assert.Equal(before, dir.Snapshot());
    }

    [Fact]
    public void Transient_lock_on_read_is_retried()
    {
        using var dir = new TempDir();
        dir.Write("progress.json", "ok:1");
        var faulty = new FaultyFileOperations();
        faulty.Faults["ReadAllText"] = (1, 1);

        Assert.Equal(LoadStatus.Loaded, File(dir, faulty).Load().Status);
    }

    [Fact]
    public void Leftover_temp_is_kept_under_orphan_name_on_load()
    {
        using var dir = new TempDir();
        dir.Write("progress.json", "ok:1");
        dir.Write("progress.json.tmp", "ok:latest");

        File(dir).Load();

        Assert.Null(dir.Read("progress.json.tmp"));
        var orphan = Assert.Single(dir.Names(), n => n.StartsWith("progress.json.tmp.orphan-", StringComparison.Ordinal));
        Assert.Equal("ok:latest", dir.Read(orphan));
    }

    [Fact]
    public void Recovery_failing_midway_is_unavailable_and_backup_is_kept()
    {
        using var dir = new TempDir();
        dir.Write("progress.json", "broken-main");
        dir.Write("progress.bak.json", "ok:0");
        var faulty = new FaultyFileOperations();
        faulty.Faults["Move"] = (1, int.MaxValue);

        var load = File(dir, faulty).Load();

        Assert.Equal(LoadStatus.Unavailable, load.Status);
        Assert.Equal("ok:0", dir.Read("progress.bak.json"));
        Assert.Equal(LoadStatus.RecoveredFromBackup, File(dir).Load().Status); // 次の起動でまた復旧できる
    }

    // ---- 退避して保存 ----

    [Fact]
    public void Evacuate_keeps_both_corrupt_files_even_in_same_millisecond()
    {
        using var dir = new TempDir();
        dir.Write("progress.json", "broken-main");
        dir.Write("progress.bak.json", "broken-bak");

        Assert.True(File(dir).EvacuateAndSave("ok:new").Succeeded);

        Assert.Equal("ok:new", dir.Read("progress.json"));
        Assert.Null(dir.Read("progress.bak.json"));
        var kept = dir.Names().Where(n => n.Contains(".corrupt-", StringComparison.Ordinal)).Select(dir.Read).Order().ToList();
        Assert.Equal(["broken-bak", "broken-main"], kept);
    }

    [Fact]
    public void Same_evacuation_name_gets_a_suffix()
    {
        using var dir = new TempDir();
        var time = new FixedTimeProvider(Now);
        dir.Write("progress.json", "broken-1");
        File(dir, time: time).EvacuateAndSave("ok:1");
        dir.Write("progress.json", "broken-2");

        Assert.True(File(dir, time: time).EvacuateAndSave("ok:2").Succeeded);

        var kept = dir.Names().Where(n => n.StartsWith("progress.json.corrupt-", StringComparison.Ordinal)).Select(dir.Read).Order().ToList();
        Assert.Equal(["broken-1", "broken-2"], kept);
    }

    [Fact]
    public void Evacuation_failure_saves_nothing()
    {
        using var dir = new TempDir();
        dir.Write("progress.json", "broken-main");
        dir.Write("progress.bak.json", "broken-bak");
        var faulty = new FaultyFileOperations();
        faulty.Faults["Move"] = (2, int.MaxValue); // 1つ目の退避は成功、2つ目で失敗

        Assert.False(File(dir, faulty).EvacuateAndSave("ok:new").Succeeded);

        Assert.DoesNotContain("ok:new", dir.Snapshot().Values);
        Assert.Contains("broken-bak", dir.Snapshot().Values);
        Assert.Contains("broken-main", dir.Snapshot().Values);
    }

    [Fact]
    public void Relative_directory_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new AtomicJsonFile<string>("relative/dir", "progress.json", Read));
    }
}
