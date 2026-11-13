using System.Text;

namespace EikenSpelling.Persistence.Tests;

/// <summary>テストごとの一時フォルダ。並列でも衝突しないよう Guid で作り、終わったら消す。実際の %APPDATA% は使わない。</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "eiken-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Write(string name, string content) => System.IO.File.WriteAllText(File(name), content, new UTF8Encoding(false));

    public string? Read(string name) => System.IO.File.Exists(File(name)) ? System.IO.File.ReadAllText(File(name)) : null;

    public string[] Names() => Directory.GetFiles(Path).Select(System.IO.Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

    /// <summary>全ファイルの名前と中身(変更が無いことの確認用)。</summary>
    public IReadOnlyDictionary<string, string> Snapshot() =>
        Names().ToDictionary(n => n, n => System.IO.File.ReadAllText(File(n)));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 後始末の失敗でテストを落とさない
        }
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
}

/// <summary>
/// 指定した操作の指定した回目(1始まり)から失敗させる実装。それ以外は本物の System.IO を使う。
/// 権限や読み取り専用属性に頼らない(Linux の root では効かないため)。
/// </summary>
internal sealed class FaultyFileOperations : IFileOperations
{
    private readonly IFileOperations _inner = SystemFileOperations.Instance;
    private readonly Dictionary<string, int> _calls = [];
    private bool _halfwayDone;

    /// <summary>操作名 → (失敗を始める回目, 失敗させる回数)。回数 int.MaxValue なら以後ずっと。</summary>
    public Dictionary<string, (int From, int Times)> Faults { get; } = [];

    /// <summary>Replace を「主 → bak は済ませ、tmp → 主 で失敗」させる(Windows で起こりうる途中状態)。</summary>
    public bool ReplaceStopsHalfway { get; set; }

    /// <summary>Length が返す値を差し替える(巨大なファイルの確認用)。ファイル名(拡張子込み)ごとに指定する。</summary>
    public Dictionary<string, long> LengthOverrides { get; } = [];

    public int Calls(string op) => _calls.GetValueOrDefault(op);

    public bool Exists(string path) => _inner.Exists(path);

    public long Length(string path)
    {
        Hit(nameof(Length));
        return LengthOverrides.TryGetValue(Path.GetFileName(path), out var length) ? length : _inner.Length(path);
    }

    public string ReadAllText(string path)
    {
        Hit(nameof(ReadAllText));
        return _inner.ReadAllText(path);
    }

    public void WriteAndFlush(string path, string content)
    {
        Hit(nameof(WriteAndFlush));
        _inner.WriteAndFlush(path, content);
    }

    public void Replace(string source, string destination, string backup)
    {
        if (ReplaceStopsHalfway)
        {
            Hit(nameof(Replace));
            // 途中の状態を作るのは1回目だけ。以後は(本物と同じく)何も変えずに失敗する
            if (!_halfwayDone)
            {
                _halfwayDone = true;
                if (File.Exists(backup))
                {
                    File.Delete(backup);
                }

                File.Move(destination, backup);
            }

            throw new IOException("置き換えの途中で失敗(テスト)");
        }

        Hit(nameof(Replace));
        _inner.Replace(source, destination, backup);
    }

    public void Move(string source, string destination)
    {
        Hit(nameof(Move));
        _inner.Move(source, destination);
    }

    public void Delete(string path)
    {
        Hit(nameof(Delete));
        _inner.Delete(path);
    }

    public void CreateDirectory(string path) => _inner.CreateDirectory(path);

    private void Hit(string op)
    {
        var n = _calls[op] = _calls.GetValueOrDefault(op) + 1;
        if (Faults.TryGetValue(op, out var f) && n >= f.From && (f.Times == int.MaxValue || n < f.From + f.Times))
        {
            throw new IOException($"{op} の {n} 回目で失敗(テスト)");
        }
    }
}
