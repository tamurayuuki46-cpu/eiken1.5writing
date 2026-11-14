namespace EikenSpelling.Core.Domain;

/// <summary>正誤判定(仕様 10.1)。大文字・小文字を区別せず、前後の空白を無視して全スペルを比べる。</summary>
public static class SpellingJudge
{
    public static string Normalize(string? input) => (input ?? "").Trim().ToLowerInvariant();

    /// <summary>空欄か(前後の空白を除くと何も無い)。画面は空欄の回答を送らない(UX 設計 Q-UX4)。判定と同じ Normalize を使う。</summary>
    public static bool IsBlank(string? input) => Normalize(input).Length == 0;

    public static bool IsCorrect(string correctWord, string? input) =>
        string.Equals(Normalize(correctWord), Normalize(input), StringComparison.Ordinal);
}
