using EikenSpelling.Core.Models;

namespace EikenSpelling.Core.Application;

/// <summary>
/// progress.json の中身。schemaVersion の正本はここだけ(v1.1 4.3)。
/// セッションは任意項目(無ければ未完了なし)。Session を持つため Application に置く(Models → Settings を作らない)。
/// </summary>
public sealed record ProgressDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>保存したときの単語データの版。今の単語データと違ってもそのまま読む(単語 ID は固定)。</summary>
    public string WordSetVersion { get; init; } = "";

    public string SourceVersion { get; init; } = "";

    public LearningProgress Progress { get; init; } = LearningProgress.Empty;

    /// <summary>未完了のセッション。完了・中止したセッションは保存しない。</summary>
    public PracticeSession? Session { get; init; }
}
