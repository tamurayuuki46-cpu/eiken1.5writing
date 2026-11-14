using EikenSpelling.Core.Domain;

namespace EikenSpelling.Core.Tests;

public class SpellingJudgeTests
{
    [Theory]
    [InlineData("accommodate", "accommodate")]
    [InlineData("accommodate", "ACCOMMODATE")] // 大文字・小文字を区別しない
    [InlineData("accommodate", "Accommodate")]
    [InlineData("accommodate", "  accommodate  ")] // 前後の空白を無視
    [InlineData("accommodate", "\taccommodate\n")]
    public void Correct_answers_are_accepted(string word, string input)
    {
        Assert.True(SpellingJudge.IsCorrect(word, input));
    }

    [Theory]
    [InlineData("accommodate", "accomodate")]
    [InlineData("accommodate", "acommodate")]
    [InlineData("accommodate", "accommodat")]
    [InlineData("accommodate", "acc ommodate")] // 途中の空白は無視しない
    [InlineData("accommodate", "")]
    [InlineData("accommodate", null)]
    public void Wrong_answers_are_rejected(string word, string? input)
    {
        Assert.False(SpellingJudge.IsCorrect(word, input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\r\n ")]
    public void Blank_inputs_are_blank_after_normalizing(string? input)
    {
        Assert.True(SpellingJudge.IsBlank(input));
    }

    [Theory]
    [InlineData("a")]
    [InlineData(" a ")] // 前後の空白を除いても文字が残る
    [InlineData("acc ommodate")]
    public void Inputs_with_letters_are_not_blank(string input)
    {
        Assert.False(SpellingJudge.IsBlank(input));
    }
}
