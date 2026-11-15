using System.Text;

using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

namespace EikenSpelling.Persistence.Tests;

/// <summary>
/// Schema v2 と旧版(v1)の原本の保護(P0-PS FA、P0-AC v0.2 B節・PS-T18)。
/// 起動だけでは書き換えない / 最初の v2 保存の前に主と(あれば)bak の原本バイトを保護 / 一致しなければ止める。
/// </summary>
public class LegacyProtectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.FromHours(9));
    private static readonly LearningSettings Settings = DefaultSettings.Create();

    private const string MainName = ProgressRepository.FileName;
    private const string BakName = "progress.bak.json";
    private const string ProtectedMain = "progress.v1-protected.main.json";
    private const string ProtectedBak = "progress.v1-protected.bak.json";

    private static ProgressRepository Repo(TempDir dir, IFileOperations? files = null) =>
        new(dir.Path, new FixedTimeProvider(Now), files, TimeSpan.Zero);

    /// <summary>v1 の文書(手で書いた形: 余白・改行も原本どおり残るかを見るため、整形をわざと崩す)。</summary>
    private static string V1Json(string word)
    {
        var progress = AnswerRecorder.Apply(
            LearningProgress.Empty,
            new AnswerEvent(new AnswerId(Guid.NewGuid(), 0, AttemptType.Normal), word, LearningMode.Japanese, false, word + "x", Now.AddDays(-1)),
            Settings).Progress;
        var json = ProgressJson.Serialize(new ProgressDocument { SchemaVersion = 1, WordSetVersion = "ws", SourceVersion = "src", Progress = progress });
        return json.Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n";
    }

    private static byte[] Bytes(TempDir dir, string name) => File.ReadAllBytes(dir.File(name));

    private static void WriteBytes(TempDir dir, string name, string text) =>
        File.WriteAllBytes(dir.File(name), [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(text)]); // BOM 付きの原本

    [Fact]
    public void Loading_valid_v1_does_not_write_anything()
    {
        using var dir = new TempDir();
        WriteBytes(dir, MainName, V1Json("tax"));
        WriteBytes(dir, BakName, V1Json("fee"));
        var before = dir.Names().ToDictionary(n => n, n => Bytes(dir, n));

        var load = Repo(dir).Load(Settings);

        Assert.Equal(LoadStatus.Loaded, load.Status);
        Assert.Equal(1, load.Document!.SchemaVersion);
        Assert.Equal(before.Keys, dir.Names());
        Assert.All(before, kv => Assert.Equal(kv.Value, Bytes(dir, kv.Key)));
    }

    [Fact]
    public void First_save_protects_v1_main_and_bak_bytes_then_writes_v2()
    {
        using var dir = new TempDir();
        WriteBytes(dir, MainName, V1Json("tax"));
        WriteBytes(dir, BakName, V1Json("fee"));
        var main = Bytes(dir, MainName);
        var bak = Bytes(dir, BakName);
        var repo = Repo(dir);
        var doc = repo.Load(Settings).Document!;

        Assert.True(repo.Save(doc).Succeeded);

        Assert.Equal(main, Bytes(dir, ProtectedMain));
        Assert.Equal(bak, Bytes(dir, ProtectedBak));
        Assert.Equal(ProgressDocument.CurrentSchemaVersion, ProgressJson.ReadSchemaVersion(dir.Read(MainName)!));
        Assert.DoesNotContain(dir.Names(), n => n.EndsWith(".partial", StringComparison.Ordinal));

        // 2回目以降の保存では保護コピーを変えない(通常のローテーションで bak が v1 の旧主になっても)
        Assert.True(repo.Save(doc).Succeeded);
        Assert.False(Repo(dir).Save(doc).Succeeded); // 読込前の別インスタンスは保存しない(従来どおり)
        var again = Repo(dir);
        Assert.True(again.Save(again.Load(Settings).Document!).Succeeded);
        Assert.Equal(main, Bytes(dir, ProtectedMain));
        Assert.Equal(bak, Bytes(dir, ProtectedBak));
    }

    [Fact]
    public void Without_bak_only_main_is_protected()
    {
        using var dir = new TempDir();
        WriteBytes(dir, MainName, V1Json("tax"));
        var main = Bytes(dir, MainName);
        var repo = Repo(dir);

        Assert.True(repo.Save(repo.Load(Settings).Document!).Succeeded);

        Assert.Equal(main, Bytes(dir, ProtectedMain));
        Assert.False(File.Exists(dir.File(ProtectedBak)));
    }

    [Fact]
    public void Already_v2_main_creates_no_protection_copy()
    {
        using var dir = new TempDir();
        dir.Write(MainName, ProgressJson.Serialize(new ProgressDocument()));
        var repo = Repo(dir);

        Assert.True(repo.Save(repo.Load(Settings).Document!).Succeeded);

        Assert.False(File.Exists(dir.File(ProtectedMain)));
        Assert.False(File.Exists(dir.File(ProtectedBak)));
    }

    [Fact]
    public void Matching_completed_copy_from_interrupted_run_is_reused()
    {
        using var dir = new TempDir();
        WriteBytes(dir, MainName, V1Json("tax"));
        WriteBytes(dir, BakName, V1Json("fee"));
        File.Copy(dir.File(MainName), dir.File(ProtectedMain)); // 主の保護だけ終えて止まった状態
        dir.Write(ProtectedBak + ".partial", "途中まで"); // bak の書きかけ(完成名ではないので使わない)
        var bak = Bytes(dir, BakName);
        var repo = Repo(dir);

        Assert.True(repo.Save(repo.Load(Settings).Document!).Succeeded);

        Assert.Equal(bak, Bytes(dir, ProtectedBak));
        Assert.False(File.Exists(dir.File(ProtectedBak + ".partial")));
    }

    [Fact]
    public void Mismatching_protection_copy_is_kept_and_a_new_copy_is_added()
    {
        using var dir = new TempDir();
        WriteBytes(dir, MainName, V1Json("tax"));
        dir.Write(ProtectedMain, V1Json("other"));
        var other = Bytes(dir, ProtectedMain);
        var main = Bytes(dir, MainName);
        var repo = Repo(dir);

        Assert.True(repo.Save(repo.Load(Settings).Document!).Succeeded);

        Assert.Equal(other, Bytes(dir, ProtectedMain)); // 既存のコピーは上書きしない
        Assert.Equal(main, Bytes(dir, "progress.v1-protected.main-2.json"));
    }

    [Fact]
    public void Recovery_from_v1_bak_after_v2_save_can_still_save()
    {
        // v1 主 A・v1 bak B → v2 保存(主 = v2、bak = A)→ 主が壊れる → bak A から復旧 → 保存できる
        using var dir = new TempDir();
        WriteBytes(dir, MainName, V1Json("tax"));
        WriteBytes(dir, BakName, V1Json("fee"));
        var a = Bytes(dir, MainName);
        var b = Bytes(dir, BakName);
        var first = Repo(dir);
        Assert.True(first.Save(first.Load(Settings).Document!).Succeeded);
        dir.Write(MainName, "{ 壊れた");

        var repo = Repo(dir);
        Assert.Equal(LoadStatus.RecoveredFromBackup, repo.Load(Settings).Status);
        var doc = repo.Load(Settings).Document!;

        Assert.True(repo.Save(doc).Succeeded);
        Assert.Equal(a, Bytes(dir, ProtectedMain));
        Assert.Equal(b, Bytes(dir, ProtectedBak));
        Assert.Equal(a, Bytes(dir, "progress.v1-protected.bak-2.json")); // 復旧後の bak(A)を別名で追加
    }

    [Fact]
    public void Returning_to_old_version_then_upgrading_again_can_still_save()
    {
        // 戻し方(protected.main を主に戻す)→ 旧版で記録を続けた別の v1 → 再び新しい版で保存できる
        using var dir = new TempDir();
        WriteBytes(dir, MainName, V1Json("tax"));
        var first = Repo(dir);
        Assert.True(first.Save(first.Load(Settings).Document!).Succeeded);
        WriteBytes(dir, MainName, V1Json("later"));
        var later = Bytes(dir, MainName);

        var repo = Repo(dir);
        Assert.True(repo.Save(repo.Load(Settings).Document!).Succeeded);

        Assert.Equal(later, Bytes(dir, "progress.v1-protected.main-2.json"));
    }

    [Fact]
    public void Failure_to_move_partial_copy_is_retried_on_next_save()
    {
        using var dir = new TempDir();
        WriteBytes(dir, MainName, V1Json("tax"));
        var main = Bytes(dir, MainName);
        var files = new FaultyFileOperations();
        files.Faults["Move"] = (1, 1);
        var repo = Repo(dir, files);
        var doc = repo.Load(Settings).Document!;

        Assert.False(repo.Save(doc).Succeeded);
        Assert.True(File.Exists(dir.File(ProtectedMain + ".partial")));
        Assert.Equal(main, Bytes(dir, MainName));

        Assert.True(repo.Save(doc).Succeeded);
        Assert.Equal(main, Bytes(dir, ProtectedMain));
        Assert.False(File.Exists(dir.File(ProtectedMain + ".partial")));
    }

    [Fact]
    public void Start_fresh_after_both_corrupt_keeps_originals_evacuated_without_protection_copy()
    {
        using var dir = new TempDir();
        dir.Write(MainName, "{ broken");
        dir.Write(BakName, "{ broken bak");
        var repo = Repo(dir);
        Assert.Equal(LoadStatus.BothCorrupt, repo.Load(Settings).Status);

        Assert.True(repo.StartFresh("ws", "src").Succeeded);

        Assert.Equal(2, dir.Names().Count(n => n.Contains(".corrupt-", StringComparison.Ordinal)));
        Assert.DoesNotContain(dir.Names(), n => n.Contains("v1-protected", StringComparison.Ordinal));
        Assert.Equal(ProgressDocument.CurrentSchemaVersion, ProgressJson.ReadSchemaVersion(dir.Read(MainName)!));
    }

    [Fact]
    public void Failure_to_write_protection_copy_stops_save_and_keeps_v1_main()
    {
        using var dir = new TempDir();
        WriteBytes(dir, MainName, V1Json("tax"));
        var main = Bytes(dir, MainName);
        var files = new FaultyFileOperations();
        files.Faults["WriteBytesAndFlush"] = (1, 1);
        var repo = Repo(dir, files);
        var doc = repo.Load(Settings).Document!;

        Assert.False(repo.Save(doc).Succeeded);
        Assert.Equal(main, Bytes(dir, MainName));
        Assert.False(File.Exists(dir.File(ProtectedMain)));

        // 次の保存(再保存)で保護してから書く
        Assert.True(repo.Save(doc).Succeeded);
        Assert.Equal(main, Bytes(dir, ProtectedMain));
    }

    [Fact]
    public void Saved_document_is_always_written_with_a_version_above_1()
    {
        // v1 として読んだ文書でも 2 以上で書く(古いアプリは schemaVersion > 1 を NewerVersion として上書きしない)
        using var dir = new TempDir();
        var repo = Repo(dir);
        repo.Load(Settings);
        Assert.True(repo.Save(new ProgressDocument { SchemaVersion = 1 }).Succeeded);

        Assert.True(ProgressJson.ReadSchemaVersion(dir.Read(MainName)!) > 1);
    }
}
