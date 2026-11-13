using System.Text.Json.Serialization;

namespace EikenSpelling.Core.Models;

/// <summary>穴あき位置の決め方。手動指定は自動生成より優先する。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BlankSource>))]
public enum BlankSource
{
    Auto,
    Manual,
}

/// <summary>
/// 単語データ1件。Id は取り込み順に依存させず固定する(通常は小文字の単語そのもの)。
/// </summary>
public sealed record WordEntry
{
    public required string Id { get; init; }

    /// <summary>正解のスペル。</summary>
    public required string Word { get; init; }

    /// <summary>日本語モードで表示する意味。判定には使わない。</summary>
    public string Ja { get; init; } = "";

    /// <summary>穴あき表示。隠す文字は '_'。例: "acc__modate"。null なら穴あきモード不可。</summary>
    public string? Blanks { get; init; }

    public BlankSource BlankSource { get; init; } = BlankSource.Auto;

    /// <summary>出典リスト名(NGSL / NAWL / BSL など)。重複語は1件に統合して配列で持つ。</summary>
    public IReadOnlyList<string> Sources { get; init; } = [];

    /// <summary>新規出題の優先度。1 が最優先。</summary>
    public int PriorityTier { get; init; } = 3;

    /// <summary>元リストでの頻度順位。小さいほど高頻度。不明なら null。</summary>
    public int? FrequencyRank { get; init; }

    public bool JapaneseEligible { get; init; } = true;

    public bool AudioEligible { get; init; } = true;
}

/// <summary>単語データファイル全体。</summary>
public sealed record WordSet
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string WordSetVersion { get; init; } = "";

    public string SourceVersion { get; init; } = "";

    public IReadOnlyList<WordEntry> Words { get; init; } = [];
}
