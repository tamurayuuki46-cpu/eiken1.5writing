using System.Diagnostics;

using EikenSpelling.Core.Application;
using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;
using EikenSpelling.Presentation.Screens;

namespace EikenSpelling.Presentation.Tests;

/// <summary>同梱する単語データ(word-data requirements D2〜D10)。道具 tools/EikenSpelling.WordData で作ったもの。</summary>
public class BundledWordsTests
{
    /// <summary>仮データ(骨組み)の18語。新しいデータでも同じ ID で残す(学習記録を保つ。D9)。</summary>
    private static readonly string[] SampleIds =
    [
        "necessary", "environment", "government", "recommend", "definitely", "separate", "embarrass", "privilege", "maintenance",
        "receive", "believe", "committee", "opportunity", "possession", "tomorrow", "independent", "immediately", "occurrence",
    ];

    private static string Json => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "words.json"));

    private static readonly Lazy<WordSet> Set = new(() => WordSetLoader.Parse(Json));

    [Fact]
    public void Bundled_words_parse_and_validate_without_errors()
    {
        var set = Set.Value;

        Assert.Equal("2026.11-5k-1", set.WordSetVersion);
        Assert.Equal("NGSL-1.2_NAWL-1.2_BSL-1.2", set.SourceVersion);
        Assert.InRange(set.Words.Count, 4500, 5500); // 約5,000語(水増しはしない。仕様 v1.1 2.2)
        Assert.DoesNotContain(WordSetValidator.Validate(set), i => i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Every_bundled_word_has_a_translation_valid_blanks_sources_and_lowercase_letters()
    {
        foreach (var w in Set.Value.Words)
        {
            Assert.Equal(w.Word, w.Id);
            Assert.True(w.Word.Length >= 3 && w.Word.All(char.IsAsciiLetterLower), $"{w.Word} は3文字以上の英小文字だけであること");
            Assert.False(string.IsNullOrWhiteSpace(w.Ja), $"{w.Word} に訳がない");
            Assert.Null(WordSetValidator.ValidateBlanks(w.Word, w.Blanks!));
            Assert.NotEmpty(w.Sources);
            Assert.All(w.Sources, s => Assert.Contains(s, new[] { "NGSL", "NAWL", "BSL" }));
            Assert.InRange(w.PriorityTier, 1, 3);
        }
    }

    [Fact]
    public void Sample_words_keep_their_ids_and_manual_blanks()
    {
        var byId = Set.Value.Words.ToDictionary(w => w.Id);

        foreach (var id in SampleIds)
        {
            Assert.True(byId.TryGetValue(id, out var w), $"{id} が無い");
            Assert.Equal(BlankSource.Manual, w!.BlankSource);
        }
    }

    [Fact]
    public void Tier1_is_within_the_spec_range_and_translations_are_unique()
    {
        var words = Set.Value.Words;

        Assert.InRange(words.Count(w => w.PriorityTier == 1), 300, 800); // 仕様 v1.1 14.3
        Assert.Equal(words.Count, words.Select(w => w.Ja).Distinct(StringComparer.Ordinal).Count()); // 同じ訳なし → 全語が日本語モード可
        Assert.All(words, w => Assert.True(w.JapaneseEligible));
    }

    [Fact]
    public void Excluded_function_words_are_not_bundled()
    {
        var ids = Set.Value.Words.Select(w => w.Id).ToHashSet();

        foreach (var word in new[] { "the", "and", "they", "would", "yeah", "anti", "mister", "amongst", "whilst", "adviser" })
        {
            Assert.DoesNotContain(word, ids);
        }

        foreach (var word in new[] { "tax", "law", "although", "despite", "whether", "thus", "may" }) // 短い内容語・つなぎの語は残す
        {
            Assert.Contains(word, ids);
        }
    }

    [Fact]
    public void Loading_validating_and_selecting_from_all_words_takes_under_one_second()
    {
        var json = Json;
        var settings = DefaultSettings.Create();
        var modes = new HashSet<LearningMode> { LearningMode.Japanese, LearningMode.Blank };
        _ = QuestionSelector.Select(WordSetLoader.Parse(json), new LearningProgress(), settings, modes, DateTimeOffset.UtcNow, new Random(1)); // JIT のぶんを除く

        var watch = Stopwatch.StartNew();
        var set = WordSetLoader.Parse(json);
        var issues = WordSetValidator.Validate(set);
        var selection = QuestionSelector.Select(set, new LearningProgress(), settings, modes, DateTimeOffset.UtcNow, new Random(1));
        watch.Stop();

        Assert.DoesNotContain(issues, i => i.Severity == ValidationSeverity.Error);
        Assert.NotEmpty(selection.Plan);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"{watch.ElapsedMilliseconds} ms かかった(目安 1 秒。仕様 v1.1 16.3)");
    }

    [Fact]
    public void A_practice_can_be_started_with_the_bundled_words_and_tier1_comes_first()
    {
        using var env = new ShellEnv { WordsJson = Json };
        var shell = env.Start();

        var q = shell.StartPractice(env.Time);

        Assert.Equal(5, q.Total);
        var tier1 = Set.Value.Words.Where(w => w.PriorityTier == 1).Select(w => w.Id).ToHashSet();
        Assert.Contains(q.WordForTests, tier1); // 新規は Tier 1 から(初回なので全問が新規)
    }
}
