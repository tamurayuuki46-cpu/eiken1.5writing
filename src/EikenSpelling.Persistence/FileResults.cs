namespace EikenSpelling.Persistence;

/// <summary>読込の結果(design.md 4章の表)。</summary>
public enum LoadStatus
{
    /// <summary>主ファイルを読めた。</summary>
    Loaded,

    /// <summary>主ファイルもバックアップも無い(初回)。</summary>
    FirstRun,

    /// <summary>主ファイルが無い・壊れていたので、バックアップから復旧した。最後の保存以降の記録が失われた可能性がある。</summary>
    RecoveredFromBackup,

    /// <summary>主ファイルとバックアップの両方が壊れている。何も書き換えていない。</summary>
    BothCorrupt,

    /// <summary>新しい版のアプリで作られたファイル。上書きも復旧もしない。</summary>
    NewerVersion,

    /// <summary>ロックや権限で読めない。壊れているとは扱わず、何も書き換えていない。</summary>
    Unavailable,
}

/// <summary>ファイル1つの読み方の結果(変換と検査を含む)。</summary>
public enum ReadKind
{
    Ok,
    Corrupt,
    Newer,
}

public sealed record FileRead<T>(ReadKind Kind, T? Value, IReadOnlyList<string> Warnings, string? Reason)
{
    public static FileRead<T> Ok(T value, IReadOnlyList<string>? warnings = null) => new(ReadKind.Ok, value, warnings ?? [], null);

    public static FileRead<T> Corrupt(string reason) => new(ReadKind.Corrupt, default, [], reason);

    public static FileRead<T> Newer(string reason) => new(ReadKind.Newer, default, [], reason);
}

/// <summary>読込の結果。理由・警告にはファイルのパスや誤答入力を含めない。</summary>
public sealed record FileLoad<T>(LoadStatus Status, T? Value, IReadOnlyList<string> Warnings, string? Reason);

/// <summary>保存の結果。失敗しても主ファイルとバックアップは前の内容のまま(Replace の途中失敗を除く。design.md 4章)。</summary>
public sealed record SaveResult(bool Succeeded, string? Reason)
{
    public static readonly SaveResult Success = new(true, null);

    public static SaveResult Failure(string reason) => new(false, reason);
}
