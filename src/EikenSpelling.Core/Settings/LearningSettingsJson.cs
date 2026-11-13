using System.Text.Json;

namespace EikenSpelling.Core.Settings;

/// <summary>
/// settings.json の文字列との変換。ファイルの読み書き・バックアップは WPF 側の JsonSettingsRepository が行う。
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
    };

    public static string Serialize(LearningSettings settings) => JsonSerializer.Serialize(settings, Options);

    /// <summary>読み込めない JSON なら InvalidDataException。値の妥当性は SettingsValidator で別に確認する。</summary>
    public static LearningSettings Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<LearningSettings>(json, Options)
                ?? throw new InvalidDataException("設定ファイルが空です。");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("設定ファイルを読み込めません。", ex);
        }
    }
}
