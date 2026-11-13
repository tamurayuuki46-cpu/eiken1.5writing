using EikenSpelling.Core.Models;

namespace EikenSpelling.Core.Domain;

public enum ValidationSeverity
{
    /// <summary>出題すると誤った問題になる。取り込みを止めるべきもの。</summary>
    Error,

    /// <summary>出題はできるが、確認したほうがよいもの。</summary>
    Warning,
}

public sealed record ValidationIssue(ValidationSeverity Severity, string WordId, string Code, string Message);

/// <summary>
/// 単語データの検証(仕様 手順2)。重複・訳なし・不正文字・穴あきの不整合を検出する。
/// </summary>
public static class WordSetValidator
{
    public const char BlankChar = '_';

    public static IReadOnlyList<ValidationIssue> Validate(WordSet set)
    {
        var issues = new List<ValidationIssue>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var seenWords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in set.Words)
        {
            var id = entry.Id ?? "";

            if (string.IsNullOrWhiteSpace(id))
            {
                issues.Add(new(ValidationSeverity.Error, id, "id.empty", "idが空です。"));
            }
            else if (!seenIds.Add(id))
            {
                issues.Add(new(ValidationSeverity.Error, id, "id.duplicate", $"id「{id}」が重複しています。"));
            }

            var word = entry.Word ?? "";
            if (string.IsNullOrWhiteSpace(word))
            {
                issues.Add(new(ValidationSeverity.Error, id, "word.empty", "wordが空です。"));
                continue;
            }

            if (!IsValidSpelling(word))
            {
                issues.Add(new(ValidationSeverity.Error, id, "word.invalidChar",
                    $"「{word}」に使えない文字があります(英字・ハイフン・アポストロフィのみ)。"));
            }

            if (seenWords.TryGetValue(word, out var firstId))
            {
                issues.Add(new(ValidationSeverity.Error, id, "word.duplicate",
                    $"「{word}」はid「{firstId}」と重複しています。1件に統合してください。"));
            }
            else
            {
                seenWords[word] = id;
            }

            if (entry.JapaneseEligible && string.IsNullOrWhiteSpace(entry.Ja))
            {
                issues.Add(new(ValidationSeverity.Error, id, "ja.missing",
                    "日本語モード可なのに日本語訳がありません。"));
            }

            if (entry.Blanks is { } blanks)
            {
                var blankError = ValidateBlanks(word, blanks);
                if (blankError is not null)
                {
                    issues.Add(new(ValidationSeverity.Error, id, "blanks.invalid", blankError));
                }
            }

            if (!MasteryRules.HasMasteryMode(entry))
            {
                // Q33: 克服対象モードで出題できない語は克服判定ができないので収録対象外。
                issues.Add(new(ValidationSeverity.Warning, id, "mode.noMasteryMode",
                    "克服対象モード(日本語・音声)のどちらでも出題できないため、克服判定ができません(Q33により収録対象外)。"));
            }
        }

        return issues;
    }

    /// <summary>英字で始まり、英字・ハイフン・アポストロフィだけからなるか。</summary>
    public static bool IsValidSpelling(string word)
    {
        if (word.Length == 0 || !IsAsciiLetter(word[0]))
        {
            return false;
        }

        foreach (var c in word)
        {
            if (!IsAsciiLetter(c) && c != '-' && c != '\'')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>穴あき表示が単語と整合しているか。問題があれば理由、なければ null。</summary>
    public static string? ValidateBlanks(string word, string blanks)
    {
        if (blanks.Length != word.Length)
        {
            return $"穴あき「{blanks}」の文字数({blanks.Length})が単語「{word}」({word.Length})と一致しません。";
        }

        var blankCount = 0;
        for (var i = 0; i < word.Length; i++)
        {
            if (blanks[i] == BlankChar)
            {
                blankCount++;
            }
            else if (char.ToLowerInvariant(blanks[i]) != char.ToLowerInvariant(word[i]))
            {
                return $"穴あき「{blanks}」の{i + 1}文字目が単語「{word}」と一致しません。";
            }
        }

        if (blankCount == 0)
        {
            return $"穴あき「{blanks}」に隠す文字がありません。";
        }

        if (blankCount == word.Length)
        {
            return $"穴あき「{blanks}」がすべて隠れていて手がかりになりません。";
        }

        return null;
    }

    private static bool IsAsciiLetter(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');
}
