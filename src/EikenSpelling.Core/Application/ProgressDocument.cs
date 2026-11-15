using EikenSpelling.Core.Models;

namespace EikenSpelling.Core.Application;

/// <summary>
/// progress.json の中身。schemaVersion の正本はここだけ(v1.1 4.3)。
/// セッションは任意項目(無ければ未完了なし)。Session を持つため Application に置く(Models → Settings を作らない)。
/// </summary>
public sealed record ProgressDocument
{
    /// <summary>
    /// 2: セッションにスキップの台帳(skips)を追加(2026-11-15、P0-PS)。1 も読む(skips なし = 空)。保存は常にこの版。
    /// 古いアプリは 2 を NewerVersion として上書きしない(台帳を知らずに採点の位置を取り違えないため)。
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>保存したときの単語データの版。今の単語データと違ってもそのまま読む(単語 ID は固定)。</summary>
    public string WordSetVersion { get; init; } = "";

    public string SourceVersion { get; init; } = "";

    public LearningProgress Progress { get; init; } = LearningProgress.Empty;

    /// <summary>未完了のセッション。完了・中止したセッションは保存しない。</summary>
    public PracticeSession? Session { get; init; }
}
