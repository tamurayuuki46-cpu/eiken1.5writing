namespace EikenSpelling.Core.Models;

/// <summary>
/// progress.json の中身。schemaVersion の正本はここだけ(v1.1 4.3)。
/// セッションの進行状態は次のタスクで任意項目として足す(無ければ未完了なし)。
/// </summary>
public sealed record ProgressDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>保存したときの単語データの版。今の単語データと違ってもそのまま読む(単語 ID は固定)。</summary>
    public string WordSetVersion { get; init; } = "";

    public string SourceVersion { get; init; } = "";

    public LearningProgress Progress { get; init; } = LearningProgress.Empty;
}
