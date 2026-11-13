using System.Text;

using EikenSpelling.Core.Application;

namespace EikenSpelling.Core.Tests;

/// <summary>3体レビュー(2026-11-13)の P1 対応: 壊れた words.json は InvalidDataException だけで知らせる。</summary>
public class WordSetLoaderRobustnessTests
{
    [Theory]
    [InlineData("{ words: ")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("""{ "words": [ { "word": "tax" } ] }""")]              // 必須の id が無い
    [InlineData("""{ "words": null }""")]
    [InlineData("""{ "words": [null] }""")]
    [InlineData("""{ "words": [ { "id": null, "word": "tax" } ] }""")]
    [InlineData("""{ "words": [ { "id": "tax", "word": "tax", "sources": null } ] }""")]
    [InlineData("""{ "words": [ { "id": "tax", "word": "tax", "sources": ["NGSL", null] } ] }""")]
    public void Broken_word_data_is_reported_as_invalid_data(string json)
    {
        Assert.Throws<InvalidDataException>(() => WordSetLoader.Parse(json));
    }

    [Fact]
    public async Task Broken_stream_is_reported_as_invalid_data()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{ words: "));

        await Assert.ThrowsAsync<InvalidDataException>(() => WordSetLoader.LoadAsync(stream));
    }

    [Fact]
    public void Explicit_null_is_allowed_for_nullable_fields()
    {
        var set = WordSetLoader.Parse("""{ "words": [ { "id": "tax", "word": "tax", "blanks": null, "frequencyRank": null } ] }""");

        Assert.Null(set.Words[0].Blanks);
        Assert.Null(set.Words[0].FrequencyRank);
    }

    [Fact]
    public void Missing_optional_fields_still_get_defaults()
    {
        var set = WordSetLoader.Parse("""{ "words": [ { "id": "tax", "word": "tax" } ] }""");

        Assert.Equal("", set.Words[0].Ja);
        Assert.Empty(set.Words[0].Sources);
    }
}
