namespace EikenSpelling.Core.Domain;

public enum DiffKind
{
    /// <summary>正解にある文字が入力にない。</summary>
    Missing,

    /// <summary>入力に余分な文字がある。</summary>
    Extra,

    /// <summary>別の文字を入力している。</summary>
    Wrong,
}

/// <summary>
/// 1か所の誤り。位置はすべて1始まり。
/// Missing / Wrong は正解側の位置(CorrectPosition)、Extra は入力側の位置(InputPosition)が主な位置。
/// </summary>
public sealed record SpellingDiff(
    DiffKind Kind,
    int CorrectPosition,
    int InputPosition,
    char? Expected,
    char? Actual);

/// <summary>
/// 差分計算(仕様 10.2)。Levenshtein 距離の表を作り、編集手順を復元する。
/// 判定と同じく、大文字・小文字と前後の空白は無視する。
/// </summary>
public static class SpellingDiffService
{
    public static IReadOnlyList<SpellingDiff> Diff(string correctWord, string? input)
    {
        var correct = SpellingJudge.Normalize(correctWord);
        var typed = SpellingJudge.Normalize(input);
        var n = correct.Length;
        var m = typed.Length;

        // d[i, j] = correct の先頭 i 文字を typed の先頭 j 文字にする最小編集回数
        var d = new int[n + 1, m + 1];
        for (var i = 0; i <= n; i++) d[i, 0] = i;
        for (var j = 0; j <= m; j++) d[0, j] = j;

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = correct[i - 1] == typed[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        // 末尾から復元する。同点のときは 置換 → 不足 → 余分 → 一致 の順で選ぶ。
        // ・置換優先: 入れ替わり(recieve)を「不足+余分」ではなく「2文字違い」と報告する
        // ・不足を一致より優先: 同じ文字が続く箇所の不足を後ろ側で報告する
        //   (accommodate / accomodate → 「6文字目のm」)
        var diffs = new List<SpellingDiff>();
        int r = n, c = m;
        while (r > 0 || c > 0)
        {
            if (r > 0 && c > 0 && correct[r - 1] != typed[c - 1] && d[r, c] == d[r - 1, c - 1] + 1)
            {
                diffs.Add(new(DiffKind.Wrong, r, c, correct[r - 1], typed[c - 1]));
                r--;
                c--;
            }
            else if (r > 0 && d[r, c] == d[r - 1, c] + 1)
            {
                diffs.Add(new(DiffKind.Missing, r, c + 1, correct[r - 1], null));
                r--;
            }
            else if (c > 0 && d[r, c] == d[r, c - 1] + 1)
            {
                diffs.Add(new(DiffKind.Extra, r + 1, c, null, typed[c - 1]));
                c--;
            }
            else
            {
                if (correct[r - 1] != typed[c - 1])
                {
                    diffs.Add(new(DiffKind.Wrong, r, c, correct[r - 1], typed[c - 1]));
                }

                r--;
                c--;
            }
        }

        diffs.Reverse();
        return diffs;
    }

    /// <summary>画面表示用の日本語メッセージ。例:「6文字目のmが不足しています」。</summary>
    public static string Describe(SpellingDiff diff) => diff.Kind switch
    {
        DiffKind.Missing => $"{diff.CorrectPosition}文字目の{diff.Expected}が不足しています",
        DiffKind.Extra => $"入力の{diff.InputPosition}文字目の{diff.Actual}は不要です",
        DiffKind.Wrong => $"{diff.CorrectPosition}文字目は{diff.Actual}ではなく{diff.Expected}です",
        _ => throw new ArgumentOutOfRangeException(nameof(diff)),
    };
}
