namespace EikenSpelling.Core.Domain;

/// <summary>穴あきを作った規則(検証レポートの内訳に使う)。</summary>
public enum BlankRule
{
    /// <summary>3文字以下: 2文字目を隠す。</summary>
    Short,

    /// <summary>二重子音(cc・mm・ss など)。最大2か所。</summary>
    DoubleConsonant,

    /// <summary>-able/-ible・-ance/-ence・-ancy/-ency の母音、-ant/-ent の母音(6文字以上)。</summary>
    Suffix,

    /// <summary>ie / ei の2文字。</summary>
    IeEi,

    /// <summary>黙字(kn-・wr-・ps-・gn- の先頭、-mb の b、gh)。</summary>
    SilentLetter,

    /// <summary>中ほどの母音(y を含む)。8文字以上は2つ。</summary>
    MiddleVowel,

    /// <summary>母音が無い: 中ほどの文字。</summary>
    Fallback,
}

public sealed record GeneratedBlanks(string Blanks, BlankRule Rule);

/// <summary>
/// 穴あき表示の自動生成(仕様 v1.1 5章、word-data design 4.3)。規則を上から試し、最初に当たったものを使う。
/// 隠すのは1文字以上・全体の半分まで(2文字の語は1文字)。生成物は必ず WordSetValidator.ValidateBlanks を通る。
/// 手動の指定を優先するのは呼び出し側(単語データを作る道具)。同じ入力からは同じ結果。
/// </summary>
public static class BlankGenerator
{
    private const string Vowels = "aeiouy";
    private const int MaxDoublePairs = 2;
    private const int MinSuffixLength = 6;
    private const int LongWord = 8;

    private static readonly string[] SuffixesHideFourthFromEnd = ["able", "ible", "ance", "ence", "ancy", "ency"];
    private static readonly string[] SuffixesHideThirdFromEnd = ["ant", "ent"];
    private static readonly string[] SilentPrefixes = ["kn", "wr", "ps", "gn"];

    public static GeneratedBlanks Generate(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (word.Length < 2 || !word.All(char.IsAsciiLetter))
        {
            throw new ArgumentException("英字だけの2文字以上の語にしてください。", nameof(word));
        }

        var lower = word.ToLowerInvariant();
        var limit = Math.Max(1, word.Length / 2);
        var (positions, rule) = Choose(lower, limit);
        var chars = word.ToCharArray();
        foreach (var i in positions)
        {
            chars[i] = WordSetValidator.BlankChar;
        }

        return new GeneratedBlanks(new string(chars), rule);
    }

    private static (int[] Positions, BlankRule Rule) Choose(string w, int limit)
    {
        if (w.Length <= 3)
        {
            return ([1], BlankRule.Short);
        }

        if (DoubleConsonants(w, limit) is { Length: > 0 } doubles)
        {
            return (doubles, BlankRule.DoubleConsonant);
        }

        if (Suffix(w) is { } suffix)
        {
            return ([suffix], BlankRule.Suffix);
        }

        if (IeEi(w) is { } ie)
        {
            return ([ie, ie + 1], BlankRule.IeEi);
        }

        if (Silent(w) is { Length: > 0 } silent && silent.Length <= limit)
        {
            return (silent, BlankRule.SilentLetter);
        }

        if (MiddleVowels(w) is { Length: > 0 } vowels)
        {
            return (vowels, BlankRule.MiddleVowel);
        }

        return ([(w.Length - 1) / 2], BlankRule.Fallback); // 中央(偶数長は前寄り。MiddleVowels と同じ数え方)
    }

    private static bool IsVowel(char c) => Vowels.Contains(c, StringComparison.Ordinal);

    /// <summary>先頭以外の同じ子音の連続。左から最大2か所、隠す数が上限を超えない範囲で。</summary>
    private static int[] DoubleConsonants(string w, int limit)
    {
        var positions = new List<int>();
        var pairs = 0;
        for (var i = 1; i + 1 < w.Length && pairs < MaxDoublePairs; i++)
        {
            if (w[i] == w[i + 1] && !IsVowel(w[i]) && positions.Count + 2 <= limit)
            {
                positions.Add(i);
                positions.Add(i + 1);
                pairs++;
                i++; // 3文字の連続(例: 架空の sss)で重ならないよう飛ばす
            }
        }

        return [.. positions];
    }

    private static int? Suffix(string w)
    {
        if (w.Length < MinSuffixLength)
        {
            return null;
        }

        if (SuffixesHideFourthFromEnd.Any(s => w.EndsWith(s, StringComparison.Ordinal)))
        {
            return w.Length - 4;
        }

        return SuffixesHideThirdFromEnd.Any(s => w.EndsWith(s, StringComparison.Ordinal)) ? w.Length - 3 : null;
    }

    private static int? IeEi(string w)
    {
        for (var i = 1; i + 1 < w.Length; i++)
        {
            if ((w[i] == 'i' && w[i + 1] == 'e') || (w[i] == 'e' && w[i + 1] == 'i'))
            {
                return i;
            }
        }

        return null;
    }

    private static int[] Silent(string w)
    {
        if (SilentPrefixes.Any(p => w.StartsWith(p, StringComparison.Ordinal)))
        {
            return [0];
        }

        if (w.EndsWith("mb", StringComparison.Ordinal))
        {
            return [w.Length - 1];
        }

        var gh = w.IndexOf("gh", 1, StringComparison.Ordinal);
        return gh > 0 ? [gh, gh + 1] : [];
    }

    /// <summary>先頭と末尾を除く母音のうち、中央に近いもの(同じ距離なら前)。8文字以上は2つ。位置の順に返す。</summary>
    private static int[] MiddleVowels(string w)
    {
        var center = (w.Length - 1) / 2.0;
        var count = w.Length >= LongWord ? 2 : 1;
        return [.. Enumerable.Range(1, w.Length - 2)
            .Where(i => IsVowel(w[i]))
            .OrderBy(i => Math.Abs(i - center))
            .ThenBy(i => i)
            .Take(count)
            .Order()];
    }
}
