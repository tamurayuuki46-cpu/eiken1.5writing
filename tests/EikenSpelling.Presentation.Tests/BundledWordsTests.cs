using EikenSpelling.Core.Application;
using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;
using EikenSpelling.Presentation.Screens;

namespace EikenSpelling.Presentation.Tests;

/// <summary>同梱する仮の単語データ(design 4.4)。</summary>
public class BundledWordsTests
{
    private static string Json => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "words.json"));

    [Fact]
    public void Bundled_words_parse_and_validate_without_errors()
    {
        var set = WordSetLoader.Parse(Json);

        Assert.StartsWith("sample-", set.WordSetVersion, StringComparison.Ordinal);
        Assert.InRange(set.Words.Count, 10, 30);
        Assert.DoesNotContain(WordSetValidator.Validate(set), i => i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Every_bundled_word_has_a_source_blanks_of_the_same_length_and_no_space_or_capital()
    {
        foreach (var w in WordSetLoader.Parse(Json).Words)
        {
            Assert.NotEmpty(w.Sources);
            Assert.All(w.Sources, s => Assert.Contains(s, new[] { "NGSL", "NAWL", "BSL" }));
            Assert.Equal(BlankSource.Manual, w.BlankSource);
            Assert.Equal(w.Word.Length, w.Blanks!.Length);
            Assert.Contains('_', w.Blanks);
            Assert.All(w.Word, c => Assert.True(char.IsAsciiLetterLower(c), $"{w.Word} に空白・大文字・記号がある"));
            Assert.NotEqual("", w.Ja);
        }
    }

    [Fact]
    public void A_practice_can_be_started_with_the_bundled_words()
    {
        using var env = new ShellEnv { WordsJson = Json };
        var shell = env.Start();

        var q = shell.StartPractice(env.Time);

        Assert.Equal(5, q.Total);
    }
}
