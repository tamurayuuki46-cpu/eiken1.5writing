using EikenSpelling.Core.Domain;

namespace EikenSpelling.Persistence;

/// <summary>
/// JSON ファイル1つの安全な保存・読込・復旧(design.md 4章)。主・バックアップ・一時ファイルは同じフォルダに置く。
/// 読み方(変換と検査)は呼び出し側から渡す。
/// </summary>
public sealed class AtomicJsonFile<T>
{
    public const long MaxFileBytes = 50L * 1024 * 1024;

    public const int MaxAttempts = 3;

    private readonly Func<string, FileRead<T>> _read;
    private readonly IFileOperations _files;
    private readonly TimeProvider _time;
    private readonly TimeSpan _retryDelay;

    public AtomicJsonFile(
        string directory, string fileName, Func<string, FileRead<T>> read,
        IFileOperations? files = null, TimeProvider? time = null, TimeSpan? retryDelay = null)
    {
        if (!Path.IsPathFullyQualified(directory))
        {
            throw new ArgumentException("保存先フォルダは絶対パスで指定してください。", nameof(directory));
        }

        Directory = Path.GetFullPath(directory);
        var name = Path.GetFileNameWithoutExtension(fileName);
        MainPath = Path.Combine(Directory, fileName);
        BackupPath = Path.Combine(Directory, name + ".bak.json");
        TempPath = MainPath + ".tmp";
        _read = read;
        _files = files ?? SystemFileOperations.Instance;
        _time = time ?? TimeProvider.System;
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(100);
    }

    public string Directory { get; }

    public string MainPath { get; }

    public string BackupPath { get; }

    public string TempPath { get; }

    /// <summary>読込と復旧(design.md 4章の表)。BothCorrupt・NewerVersion・Unavailable では何も書き換えない。</summary>
    public FileLoad<T> Load()
    {
        var main = ReadFile(MainPath);
        switch (main.State)
        {
            case FileState.Ok:
                KeepOrphanTemp();
                return new(LoadStatus.Loaded, main.Read!.Value, main.Read.Warnings, null);
            case FileState.Unavailable:
                return Unavailable("主ファイルを読めません(ロックまたは権限)。");
            case FileState.Newer:
                return new(LoadStatus.NewerVersion, default, [], main.Read!.Reason);
        }

        var backup = ReadFile(BackupPath);
        switch (backup.State)
        {
            case FileState.Ok:
                return Recover(main.State == FileState.Corrupt, backup);
            case FileState.Unavailable:
                return Unavailable("バックアップを読めません(ロックまたは権限)。");
            case FileState.Newer:
                return new(LoadStatus.NewerVersion, default, [], backup.Read!.Reason);
            case FileState.Missing when main.State == FileState.Missing:
                KeepOrphanTemp();
                return new(LoadStatus.FirstRun, default, [], null);
            default:
                return new(LoadStatus.BothCorrupt, default, [], "主ファイルとバックアップの両方が壊れています。");
        }
    }

    /// <summary>
    /// 安全な保存: 一時ファイルに書いて flush → 読み戻して検証 → 「主 → bak、tmp → 主」を一度に置き換え(初回は移動)。
    /// 失敗しても例外は投げず、理由(パス・内容を含めない)を返す。
    /// </summary>
    public SaveResult Save(string content)
    {
        try
        {
            Retry(() => _files.WriteAndFlush(TempPath, content));
            if (SafeRead(Retry(() => _files.ReadAllText(TempPath))).Kind != ReadKind.Ok)
            {
                TryDelete(TempPath);
                return SaveResult.Failure("保存する内容を読み戻して確かめたところ、正しくありませんでした。");
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            TryDelete(TempPath);
            return SaveResult.Failure("一時ファイルに書き込めませんでした。");
        }

        try
        {
            if (_files.Exists(MainPath))
            {
                // 途中で止まって主が無くなったら繰り返さない(次の読込でバックアップから復旧し、tmp は orphan 名で残す)
                Retry(() => _files.Replace(TempPath, MainPath, BackupPath), stopIf: () => !_files.Exists(MainPath));
            }
            else
            {
                Retry(() => _files.Move(TempPath, MainPath));
            }

            return SaveResult.Success;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return SaveResult.Failure(_files.Exists(MainPath)
                ? "ファイルを置き換えられませんでした。前回の内容は残っています。"
                : "置き換えの途中で止まりました。次回の起動でバックアップから復旧します。");
        }
    }

    /// <summary>主・バックアップを退避名へ移動し、すべて成功したときだけ content を保存する(新規開始・設定の作り直し)。</summary>
    public SaveResult EvacuateAndSave(string content)
    {
        // 先に中身を確かめる(退避した後で保存に失敗すると主もバックアップも無くなるため)
        if (SafeRead(content).Kind != ReadKind.Ok)
        {
            return SaveResult.Failure("新しく保存する内容が正しくないため、何も変えていません。");
        }

        try
        {
            if (_files.Exists(TempPath))
            {
                // 最新の記録が残っているかもしれないので消さずに残す
                var orphan = EvacuationPath(TempPath, ".orphan-", "");
                Retry(() => _files.Move(TempPath, orphan));
            }

            foreach (var path in new[] { MainPath, BackupPath })
            {
                if (_files.Exists(path))
                {
                    var target = EvacuationPath(path, ".corrupt-", ".json");
                    Retry(() => _files.Move(path, target));
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return SaveResult.Failure("壊れたファイルを退避できなかったため、新しく保存していません。");
        }

        return Save(content);
    }

    /// <summary>
    /// バックアップから復旧する: bak の中身を tmp に書く → 壊れた主を退避名へ移動 → tmp を主へ移動。
    /// 次の保存では正常な主が bak に回る。途中で失敗したら Unavailable(bak は変えないので次の起動で再び復旧できる)。
    /// </summary>
    private FileLoad<T> Recover(bool mainIsCorrupt, ReadResult backup)
    {
        KeepOrphanTemp();
        if (_files.Exists(TempPath))
        {
            // 残っていた tmp(最後の1問分の可能性)を上書きしない
            return Unavailable("前回の一時ファイルを退避できないため、復旧を中止しました。");
        }

        try
        {
            Retry(() => _files.WriteAndFlush(TempPath, backup.Text!));
            if (mainIsCorrupt && _files.Exists(MainPath))
            {
                var target = EvacuationPath(MainPath, ".corrupt-", ".json");
                Retry(() => _files.Move(MainPath, target));
            }

            Retry(() => _files.Move(TempPath, MainPath));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Unavailable("バックアップからの復旧を完了できませんでした。");
        }

        return new(
            LoadStatus.RecoveredFromBackup,
            backup.Read!.Value,
            backup.Read.Warnings,
            "主ファイルが壊れていたため、バックアップから復旧しました。最後の保存以降の記録が失われた可能性があります。");
    }

    private ReadResult ReadFile(string path)
    {
        if (!_files.Exists(path))
        {
            return new(FileState.Missing, null, null);
        }

        string text;
        try
        {
            if (Retry(() => _files.Length(path)) > MaxFileBytes)
            {
                return new(FileState.Corrupt, null, FileRead<T>.Corrupt("ファイルが大きすぎます。"));
            }

            text = Retry(() => _files.ReadAllText(path));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // 読めない理由が何であれ「壊れている」とは扱わない(正常なファイルをバックアップで巻き戻さないため)
            return new(FileState.Unavailable, null, null);
        }

        var read = SafeRead(text);
        var state = read.Kind switch
        {
            ReadKind.Ok => FileState.Ok,
            ReadKind.Newer => FileState.Newer,
            _ => FileState.Corrupt,
        };
        return new(state, text, read);
    }

    /// <summary>読み方(変換と検査)が例外を投げても「壊れている」として扱う。</summary>
    private FileRead<T> SafeRead(string text)
    {
        try
        {
            return _read(text);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return FileRead<T>.Corrupt("内容を読み取れません。");
        }
    }

    /// <summary>前回の保存が途中で止まって残った tmp は消さずに orphan 名で残す(失敗しても読込は続ける)。</summary>
    private void KeepOrphanTemp()
    {
        if (!_files.Exists(TempPath))
        {
            return;
        }

        try
        {
            _files.Move(TempPath, EvacuationPath(TempPath, ".orphan-", ""));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 残しておけば次の読込で再び試す
        }
    }

    /// <summary>退避名: 元のファイル名 + 印 + 日時(ミリ秒まで)。同名があれば -1, -2 … を付ける(上書きしない)。</summary>
    private string EvacuationPath(string path, string marker, string extension)
    {
        var stamp = JstClock.Now(_time).ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture);
        var baseName = Path.GetFileName(path) + marker + stamp;
        var candidate = Path.Combine(Directory, baseName + extension);
        for (var i = 1; _files.Exists(candidate); i++)
        {
            candidate = Path.Combine(Directory, $"{baseName}-{i}{extension}");
        }

        return candidate;
    }

    private void TryDelete(string path)
    {
        try
        {
            if (_files.Exists(path))
            {
                _files.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 残った tmp は次の読込で orphan 名へ移す
        }
    }

    private FileLoad<T> Unavailable(string reason) => new(LoadStatus.Unavailable, default, [], reason);

    private void Retry(Action action, Func<bool>? stopIf = null) => Retry<object?>(() =>
    {
        action();
        return null;
    }, stopIf);

    /// <summary>ロック・権限などの一時的な入出力の失敗を、少し待って最大 MaxAttempts 回まで試す。</summary>
    private TResult Retry<TResult>(Func<TResult> action, Func<bool>? stopIf = null)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return action();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       && attempt < MaxAttempts && stopIf?.Invoke() != true)
            {
                if (_retryDelay > TimeSpan.Zero)
                {
                    Thread.Sleep(_retryDelay);
                }
            }
        }
    }

    private enum FileState
    {
        Missing,
        Unavailable,
        Corrupt,
        Newer,
        Ok,
    }

    private sealed record ReadResult(FileState State, string? Text, FileRead<T>? Read);
}
