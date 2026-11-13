using EikenSpelling.Core.Application;
using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;

namespace EikenSpelling.Core.Tests;

public class WordDataTests
{
    private static WordEntry Word(string word, string ja = "意味", string? blanks = null) =>
        new() { Id = word, Word = word, Ja = ja, Blanks = blanks, Sources = ["NGSL"] };

    [Fact]
    public void Loader_reads_camel_case_json_with_defaults()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "wordSetVersion": "test",
          "words": [
            { "id": "accommodate", "word": "accommodate", "ja": "収容する", "blanks": "acc__modate",
              "blankSource": "manual", "sources": ["NGSL"], "priorityTier": 1, "frequencyRank": 1200 },
            { "id": "tax", "word": "tax", "ja": "税金" }
          ]
        }
        """;

        var set = WordSetLoader.Parse(json);

        Assert.Equal(2, set.Words.Count);
        Assert.Equal(BlankSource.Manual, set.Words[0].BlankSource);
        Assert.Equal(1, set.Words[0].PriorityTier);
        Assert.True(set.Words[1].AudioEligible); // 省略時の初期値
        Assert.Equal(3, set.Words[1].PriorityTier);
    }

    [Fact]
    public void Loader_rejects_newer_schema()
    {
        Assert.Throws<InvalidDataException>(() => WordSetLoader.Parse("""{ "schemaVersion": 99, "words": [] }"""));
    }

    [Fact]
    public void Valid_set_has_no_issues()
    {
        var set = new WordSet { Words = [Word("accommodate", blanks: "acc__modate"), Word("tax"), Word("well-being")] };

        Assert.Empty(WordSetValidator.Validate(set));
    }

    [Fact]
    public void Duplicate_words_and_ids_are_errors()
    {
        var set = new WordSet
        {
            Words = [Word("tax"), Word("tax"), new WordEntry { Id = "law2", Word = "LAW", Ja = "法" }, Word("law")],
        };

        var codes = WordSetValidator.Validate(set).Select(i => i.Code).ToList();

        Assert.Contains("id.duplicate", codes);
        Assert.Equal(2, codes.Count(c => c == "word.duplicate")); // 大文字・小文字違いも重複
    }

    [Theory]
    [InlineData("café")]
    [InlineData("two words")]
    [InlineData("1st")]
    [InlineData("-ing")]
    public void Invalid_characters_are_errors(string word)
    {
        var issues = WordSetValidator.Validate(new WordSet { Words = [Word(word)] });

        Assert.Contains(issues, i => i.Code == "word.invalidChar");
    }

    [Fact]
    public void Missing_japanese_is_error_only_when_japanese_mode_is_enabled()
    {
        var set = new WordSet
        {
            Words =
            [
                Word("tax", ja: ""),
                new WordEntry { Id = "law", Word = "law", Ja = "", JapaneseEligible = false },
            ],
        };

        var issues = WordSetValidator.Validate(set);

        Assert.Single(issues, i => i.Code == "ja.missing");
        Assert.Equal("tax", issues.Single(i => i.Code == "ja.missing").WordId);
    }

    [Theory]
    [InlineData("acc__modate", true)]
    [InlineData("ACC__MODATE", true)]
    [InlineData("acc_modate", false)] // 文字数が違う
    [InlineData("acx__modate", false)] // 隠していない文字が違う
    [InlineData("accommodate", false)] // 隠す文字がない
    [InlineData("___________", false)] // 全部隠れている
    public void Blanks_must_match_the_word(string blanks, bool valid)
    {
        Assert.Equal(valid, WordSetValidator.ValidateBlanks("accommodate", blanks) is null);
    }

    [Fact]
    public void Word_without_any_mastery_mode_is_warned()
    {
        var entry = new WordEntry { Id = "tax", Word = "tax", Ja = "税金", JapaneseEligible = false, AudioEligible = false };

        var issue = Assert.Single(WordSetValidator.Validate(new WordSet { Words = [entry] }));

        Assert.Equal(ValidationSeverity.Warning, issue.Severity);
        Assert.Equal("mode.noMasteryMode", issue.Code);
    }
}
