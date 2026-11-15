using EikenSpelling.Core.Application;

namespace EikenSpelling.Persistence;

/// <summary>
/// 旧版(schema v1)の学習記録の原本を、最初の v2 保存の前に保護する(P0-PS FA・P0-AC v0.2 B節)。
/// ・主ファイルが v1 のときだけ、主と(あれば)bak の原本バイトを役割別のコピーに残す
/// ・コピーは「.partial に書く → 読み戻してバイト一致を確かめる → 完成名へ移動」。完成名がある = 検証済み
/// ・同じ役割の完成したコピー(基本名と -2, -3 … の別名)のどれかが元と一致すれば再利用する(途中で止まった後の再起動など)
/// ・どれとも一致しなければ(bak からの復旧・旧版に戻して記録を続けた後など)、次の別名に新しく残す。既存のコピーは上書きしない
/// ・コピーは通常の読込の候補(主・bak)に混ぜず、移行後も消さない。通常のローテーションの bak を代わりにしない
/// アプリが1つだけ動いている前提(二重起動は WPF で防ぐ)。理由にパス・内容を含めない。
/// </summary>
internal sealed class LegacyOriginalProtection
{
    private readonly IFileOperations _files;
    private readonly string _mainPath;
    private readonly string _backupPath;
    private readonly string _protectedMainPath;
    private readonly string _protectedBackupPath;

    // 主ファイルが今の版だと確かめた後は読み直さない(このアプリの保存は常に今の版)
    private bool _mainIsCurrent;

    public LegacyOriginalProtection(IFileOperations files, string directory, string mainPath, string backupPath)
    {
        _files = files;
        _mainPath = mainPath;
        _backupPath = backupPath;
        var name = Path.GetFileNameWithoutExtension(mainPath);
        _protectedMainPath = Path.Combine(directory, name + ".v1-protected.main.json");
        _protectedBackupPath = Path.Combine(directory, name + ".v1-protected.bak.json");
    }

    public string ProtectedMainPath => _protectedMainPath;

    public string ProtectedBackupPath => _protectedBackupPath;

    /// <summary>保存の前に呼ぶ。保護できた・不要なら null、保存を止めるなら理由。</summary>
    public string? EnsureProtected()
    {
        if (_mainIsCurrent)
        {
            return null;
        }

        try
        {
            if (!_files.Exists(_mainPath))
            {
                return null; // 初回(主が無い)は守る原本が無い
            }

            var main = _files.ReadAllBytes(_mainPath);
            if (!IsLegacy(main))
            {
                _mainIsCurrent = true;
                return null;
            }

            if (Protect(main, _protectedMainPath) is { } mainProblem)
            {
                return mainProblem;
            }

            if (_files.Exists(_backupPath) && Protect(_files.ReadAllBytes(_backupPath), _protectedBackupPath) is { } backupProblem)
            {
                return backupProblem;
            }

            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return "旧版の学習記録の保護コピーを作れなかったため、保存していません。";
        }
    }

    /// <summary>保存が成功した後に呼ぶ(主は今の版になった)。</summary>
    public void MarkSaved() => _mainIsCurrent = true;

    private static bool IsLegacy(byte[] content)
    {
        try
        {
            return ProgressJson.ReadSchemaVersion(new System.Text.UTF8Encoding(false).GetString(content))
                   < ProgressDocument.CurrentSchemaVersion;
        }
        catch (InvalidDataException)
        {
            // 読めない主は読込で弾かれている(ここには来ない)。来たら原本として保護してから保存する(消さない側に倒す)
            return true;
        }
    }

    /// <summary>保護コピーの上限(別名をこれ以上増やさない。異常な状態で無限に増えるのを防ぐ)。</summary>
    internal const int MaxCopiesPerRole = 100;

    private string? Protect(byte[] original, string basePath)
    {
        string? target = null;
        for (var n = 1; n <= MaxCopiesPerRole; n++)
        {
            var candidate = n == 1 ? basePath : Path.ChangeExtension(basePath, null) + $"-{n}.json";
            if (!_files.Exists(candidate))
            {
                target = candidate;
                break;
            }

            if (_files.ReadAllBytes(candidate).AsSpan().SequenceEqual(original))
            {
                return null; // 検証済みのコピーがある
            }
        }

        if (target is null)
        {
            return "旧版の学習記録の保護コピーが多すぎるため、保存を止めました。";
        }

        var partial = target + ".partial";
        _files.WriteBytesAndFlush(partial, original);
        if (!_files.ReadAllBytes(partial).AsSpan().SequenceEqual(original))
        {
            return "旧版の学習記録の保護コピーを確かめられなかったため、保存していません。";
        }

        _files.Move(partial, target);
        return null;
    }
}
