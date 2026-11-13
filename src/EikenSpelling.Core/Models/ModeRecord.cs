using System.Text.Json.Serialization;

namespace EikenSpelling.Core.Models;

/// <summary>単語×モードの記録(v1.0 11.1)。通常問題と追加再練習の成績を分けて持つ。</summary>
public sealed record ModeRecord
{
    public static readonly ModeRecord Empty = new();

    public int NormalCorrect { get; init; }

    public int NormalWrong { get; init; }

    public int RetryCorrect { get; init; }

    public int RetryWrong { get; init; }

    /// <summary>最後の誤答入力(通常・再練習の両方)。</summary>
    public string? LastWrongInput { get; init; }

    public DateTimeOffset? LastWrongAt { get; init; }

    /// <summary>このモードで最後に出題された日時(通常・再練習の両方)。</summary>
    public DateTimeOffset? LastAskedAt { get; init; }

    /// <summary>通常問題の正答率。通常問題で未出題なら null(画面では「未学習」)(v1.0 11.3)。</summary>
    [JsonIgnore]
    public double? Accuracy =>
        NormalCorrect + (long)NormalWrong == 0 ? null : NormalCorrect / ((double)NormalCorrect + NormalWrong);
}
