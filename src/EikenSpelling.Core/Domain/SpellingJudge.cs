namespace EikenSpelling.Core.Domain;

/// <summary>正誤判定(仕様 10.1)。大文字・小文字を区別せず、前後の空白を無視して全スペルを比べる。</summary>
public static class SpellingJudge
{
    public static string Normalize(string? input) => (input ?? "").Trim().ToLowerInvariant();

    public static bool IsCorrect(string correctWord, string? input) =>
        string.Equals(Normalize(correctWord), Normalize(input), StringComparison.Ordinal);
}
