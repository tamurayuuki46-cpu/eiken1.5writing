using System.Text;

namespace EikenSpelling.Persistence;

/// <summary>保存・読込で使うファイル操作。テストでは途中で失敗させる実装に差し替える。</summary>
public interface IFileOperations
{
    bool Exists(string path);

    long Length(string path);

    string ReadAllText(string path);

    /// <summary>UTF-8(BOM なし)で書き、ディスクまで書き出す(Flush(true))。既存のファイルは上書き。</summary>
    void WriteAndFlush(string path, string content);

    /// <summary>「destination → backup、source → destination」を一度に行う(File.Replace)。</summary>
    void Replace(string source, string destination, string backup);

    /// <summary>移動。移動先が既にあれば失敗する(上書きしない)。</summary>
    void Move(string source, string destination);

    void Delete(string path);

    void CreateDirectory(string path);
}

/// <summary>本番の実装(System.IO)。</summary>
public sealed class SystemFileOperations : IFileOperations
{
    public static readonly SystemFileOperations Instance = new();

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public bool Exists(string path) => File.Exists(path);

    public long Length(string path) => new FileInfo(path).Length;

    public string ReadAllText(string path) => File.ReadAllText(path, Encoding.UTF8);

    public void WriteAndFlush(string path, string content)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        var bytes = Utf8NoBom.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    public void Replace(string source, string destination, string backup) =>
        File.Replace(source, destination, backup, ignoreMetadataErrors: true);

    public void Move(string source, string destination) => File.Move(source, destination, overwrite: false);

    public void Delete(string path) => File.Delete(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
}
