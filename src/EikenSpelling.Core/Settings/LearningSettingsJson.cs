using System.Text.Json;

namespace EikenSpelling.Core.Settings;

/// <summary>
/// settings.json の文字列との変換。ファイルの読み書き・バックアップは EikenSpelling.Persistence の SettingsRepository が行う。
/// 欠けた項目は初期値で補い、知らない項目は無視する。
/// </summary>
public static class LearningSettingsJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        // 明示的な null は「欠けた項目」と区別して読込エラーにする(null のまま下流へ流さない)
        RespectNullableAnnotations = true,
    };

    /// <summary>リストの要素の null(RespectNullableAnnotations では検出されない)があるか。セッションのスナップショットでも使う。</summary>
    internal static bool HasNullElements(LearningSettings settings) =>
        settings.Practice is null || settings.Mastery is null || settings.Vocabulary is null || settings.WeakWordScore is null
        || settings.Practice.ModeWeights is null || settings.Practice.QuestionCountChoices is null || settings.Practice.FlashSecondsChoices is null
        || settings.Mastery.ReviewIntervalsDays is null || settings.Mastery.ReviewLimits is null || settings.Vocabulary.EnabledSources is null
        || settings.Mastery.ReviewLimits.Any(l => l is null) || settings.Vocabulary.EnabledSources.Any(s => s is null);

    /// <summary>全体を変換する前に settingsVersion だけを読む。無ければ null(初期値の版として扱う)。</summary>
    public static int? ReadSettingsVersion(string json) => JsonVersionReader.Read(json, "settingsVersion");

    public static string Serialize(LearningSettings settings) => JsonSerializer.Serialize(settings, Options);

    /// <summary>
    /// 読み込めない JSON(壊れている・型違い・明示的な null を含む)なら InvalidDataException だけを投げる。
    /// 値の妥当性は SettingsValidator で別に確認する。
    /// </summary>
    public static LearningSettings Parse(string json)
    {
        LearningSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<LearningSettings>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("設定ファイルを読み込めません。", ex);
        }

        if (settings is null)
        {
            throw new InvalidDataException("設定ファイルが空です。");
        }

        // リストの要素の null は RespectNullableAnnotations では検出されないため個別に確認する
        if (HasNullElements(settings))
        {
            throw new InvalidDataException("設定ファイルに空(null)の要素があります。");
        }

        return settings;
    }
}
