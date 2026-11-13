namespace EikenSpelling.Core.Settings;

/// <summary>
/// 設定値として受け付ける範囲(3体レビュー 2026-11-13 で決定)。学習ルールの値ではなく、
/// 手で書き換えた settings.json が下流で桁あふれや巨大な確保を起こさないための安全な範囲。
/// </summary>
public static class SettingsLimits
{
    public const int MaxQuestionCount = 100;

    public const int MaxModeWeight = 100;

    public const int MaxRetryCount = 3;

    public const double MinFlashSeconds = 0.1;

    public const double MaxFlashSeconds = 10.0;

    public const int MaxReviewIntervalDays = 3650;
}
