using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace EikenSpelling.Core.Models;

/// <summary>単語ごとの学習記録(v1.0 11.2、v1.1 10章)。状態ごとの正しい組み合わせは design.md 2章の表を参照。</summary>
public sealed record WordProgress
{
    public required string WordId { get; init; }

    public LearningState State { get; init; } = LearningState.Normal;

    public ImmutableDictionary<LearningMode, ModeRecord> Modes { get; init; } =
        ImmutableDictionary<LearningMode, ModeRecord>.Empty;

    /// <summary>最後に通常問題で出題した日時(再練習は含めない。v1.0 9.2 の復習順に使う)。</summary>
    public DateTimeOffset? LastNormalAskedAt { get; init; }

    /// <summary>克服用の連続正答数。克服時は必要回数のまま残す(v1.1 10.3)。</summary>
    public int MasteryStreak { get; init; }

    /// <summary>最後に克服判定対象として正解した日(日本時間)。</summary>
    public DateOnly? LastEligibleCorrectDate { get; init; }

    /// <summary>最後に克服した日時。苦手に戻っても残す。</summary>
    public DateTimeOffset? MasteredAt { get; init; }

    /// <summary>次回再確認予定日(日本時間)。克服済みで最後の段階を終えたときは null。</summary>
    public DateOnly? NextReviewDate { get; init; }

    /// <summary>再確認段階。0 = 未克服、1〜間隔の数(v1.1 10.2)。</summary>
    public int ReviewStage { get; init; }

    public ModeRecord Mode(LearningMode mode) => Modes.GetValueOrDefault(mode) ?? ModeRecord.Empty;

    /// <summary>通常問題の誤答数の累計(苦手登録の回数判定に使う)。</summary>
    [JsonIgnore]
    public int NormalMistakes => Modes.Values.Sum(m => m.NormalWrong);
}
