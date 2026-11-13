using EikenSpelling.Core.Domain;

namespace EikenSpelling.Core.Tests;

public class SpellingDiffServiceTests
{
    [Fact]
    public void Correct_input_has_no_diff()
    {
        Assert.Empty(SpellingDiffService.Diff("accommodate", " Accommodate "));
    }

    [Fact]
    public void Missing_double_letter_is_reported_at_the_later_position()
    {
        // 仕様 10.2 の表示例
        var diff = Assert.Single(SpellingDiffService.Diff("accommodate", "accomodate"));

        Assert.Equal(DiffKind.Missing, diff.Kind);
        Assert.Equal(6, diff.CorrectPosition);
        Assert.Equal('m', diff.Expected);
        Assert.Equal("6文字目のmが不足しています", SpellingDiffService.Describe(diff));
    }

    [Fact]
    public void Extra_letter_is_reported_with_input_position()
    {
        var diff = Assert.Single(SpellingDiffService.Diff("necessary", "neccessary"));

        Assert.Equal(DiffKind.Extra, diff.Kind);
        Assert.Equal('c', diff.Actual);
        Assert.Equal(4, diff.InputPosition);
        Assert.Equal("入力の4文字目のcは不要です", SpellingDiffService.Describe(diff));
    }

    [Fact]
    public void Wrong_letter_is_reported_as_substitution()
    {
        var diff = Assert.Single(SpellingDiffService.Diff("separate", "seperate"));

        Assert.Equal(DiffKind.Wrong, diff.Kind);
        Assert.Equal(4, diff.CorrectPosition);
        Assert.Equal('a', diff.Expected);
        Assert.Equal('e', diff.Actual);
        Assert.Equal("4文字目はeではなくaです", SpellingDiffService.Describe(diff));
    }

    [Fact]
    public void Multiple_errors_are_listed_in_word_order()
    {
        // definitely → difinitly : 2文字目の置換と8文字目の不足
        var diffs = SpellingDiffService.Diff("definitely", "difinitly");

        Assert.Equal(2, diffs.Count);
        Assert.Equal(DiffKind.Wrong, diffs[0].Kind);
        Assert.Equal(2, diffs[0].CorrectPosition);
        Assert.Equal(DiffKind.Missing, diffs[1].Kind);
        Assert.Equal('e', diffs[1].Expected);
    }

    [Fact]
    public void Transposed_letters_count_as_two_edits()
    {
        var diffs = SpellingDiffService.Diff("receive", "recieve");

        Assert.Equal(2, diffs.Count);
        Assert.All(diffs, d => Assert.Equal(DiffKind.Wrong, d.Kind));
    }

    [Fact]
    public void Empty_input_reports_every_letter_missing()
    {
        var diffs = SpellingDiffService.Diff("tax", "");

        Assert.Equal(3, diffs.Count);
        Assert.All(diffs, d => Assert.Equal(DiffKind.Missing, d.Kind));
        Assert.Equal([1, 2, 3], diffs.Select(d => d.CorrectPosition));
    }

    [Theory]
    [InlineData("accommodate", "accomodate", 1)]
    [InlineData("environment", "enviroment", 1)]
    [InlineData("receive", "recieve", 2)]
    [InlineData("tax", "taxes", 2)]
    [InlineData("law", "", 3)]
    public void Number_of_diffs_equals_edit_distance(string word, string input, int expected)
    {
        Assert.Equal(expected, SpellingDiffService.Diff(word, input).Count);
    }
}
