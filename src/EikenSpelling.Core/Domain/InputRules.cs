namespace EikenSpelling.Core.Domain;

/// <summary>回答の入力に関する固定ルール(設定にしない。DECISIONS.md「固定ルール」)。</summary>
public static class InputRules
{
    /// <summary>記録に残す誤答入力の最大文字数(UTF-16 の文字数)。</summary>
    public const int MaxStoredInputLength = 100;

    /// <summary>上限を超えた分を切り捨てる。サロゲートペアの途中では切らない。</summary>
    public static string? TruncateForStorage(string? input)
    {
        if (input is null || input.Length <= MaxStoredInputLength)
        {
            return input;
        }

        var cut = char.IsHighSurrogate(input[MaxStoredInputLength - 1]) ? MaxStoredInputLength - 1 : MaxStoredInputLength;
        return input[..cut];
    }
}
