using EikenSpelling.Core.Application;
using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;

namespace EikenSpelling.WordData.Tests;

/// <summary>単語データを作る道具(word-data design 4章)。小さな入力で確かめる。</summary>
public class WordDataTests
{
    private static readonly BuildOptions Options = new("test-1", "src-1", AllowMissingTranslations: false);

    private static SourceWord W(string word, int? rank = null) => new(word, rank);

    private static Curation Cur(
        Dictionary<string, string>? ja = null, string[]? tier1 = null,
        Dictionary<string, string>? excluded = null, Dictionary<string, string>? blanks = null) =>
        new(ja ?? [], (tier1 ?? []).ToHashSet(), excluded ?? [], blanks ?? []);

    // ---- CSV の読込 ----

    [Fact]
    public void Stats_csv_is_read_by_column_name_and_ignores_extra_and_empty_columns()
    {
        var bsl = SourceLists.ReadBslStats("Word,BSL Rank,Band,SFI,U,,,,,,581.3\r\nmister,1,1,71.51,1416.4,,,,,,\r\n,,,,,,\r\ngoods,2,1,66.71,468.5,,,,,,\r\n");

        Assert.Equal([W("mister", 1), W("goods", 2)], bsl);
    }

    [Fact]
    public void Ngsl_stats_and_lemmatized_lists_are_read()
    {
        Assert.Equal([W("the", 1), W("be", 2)], SourceLists.ReadNgslStats("Lemma,SFI Rank,SFI,Adjusted Frequency per Million (U)\nthe,1,87.85,60910\nbe,2,86.86,48575\n"));
        Assert.Equal([W("Monday"), W("absorb")], SourceLists.ReadLemmatized("﻿Monday,Mondays\r\n\r\nabsorb,absorbs,absorbed\r\n"));
    }

    [Fact]
    public void Missing_columns_or_unreadable_rank_are_errors()
    {
        Assert.Throws<InvalidDataException>(() => SourceLists.ReadBslStats("Lemma,Rank\nx,1\n"));
        Assert.Throws<InvalidDataException>(() => SourceLists.ReadNgslStats("Lemma,SFI Rank\nx,abc\n"));
    }

    // ---- 統合・頻度順位 ----

    [Fact]
    public void Same_spelling_across_lists_is_merged_with_sources_in_list_order_and_lowercase()
    {
        var merged = WordMerger.Merge([W("Economy", 300)], [W("economy"), W("analyse")], [W("economy", 5), W("invoice", 10)]);

        var economy = Assert.Single(merged.Words, w => w.Word == "economy");
        Assert.Equal(["NGSL", "NAWL", "BSL"], economy.Sources);
        Assert.Equal(300, economy.NgslRank);
        Assert.Equal(5, economy.BslRank);
        Assert.Equal(["analyse", "economy", "invoice"], merged.Words.Select(w => w.Word));
    }

    [Fact]
    public void Words_with_non_letters_are_not_merged_and_are_reported()
    {
        var merged = WordMerger.Merge([W("o'clock", 1), W("t-shirt", 2), W("ok", 3)], [], [W("e mail", 4)]);

        Assert.Equal(["ok"], merged.Words.Select(w => w.Word));
        Assert.Equal(["o'clock", "t-shirt", "e mail"], merged.Rejected.Select(r => r.Word));
    }

    [Fact]
    public void Frequency_rank_prefers_ngsl_then_bsl_after_ngsl_and_none_for_nawl_only()
    {
        var merged = WordMerger.Merge([W("market", 100), W("monday")], [W("analyse")], [W("market", 3), W("invoice", 10)]);
        int? Rank(string word) => WordMerger.FrequencyRank(merged.Words.Single(w => w.Word == word), ngslMaxRank: 2809);

        Assert.Equal(100, Rank("market"));
        Assert.Equal(2819, Rank("invoice"));
        Assert.Null(Rank("analyse"));
        Assert.Null(Rank("monday")); // NGSL の補足(順位なし)
    }

    // ---- 組み立て ----

    private static MergeResult Sample() => WordMerger.Merge(
        [W("be", 2), W("tax", 900), W("committee", 1038), W("receive", 396), W("the", 1)],
        [W("analyse")],
        [W("invoice", 10)]);

    private static Dictionary<string, string> AllJa() => new()
    {
        ["tax"] = "税", ["committee"] = "委員会", ["receive"] = "受け取る", ["analyse"] = "分析する", ["invoice"] = "請求書", ["the"] = "その",
    };

    [Fact]
    public void Short_and_curated_words_are_excluded_with_reasons_but_short_content_words_stay()
    {
        var result = WordSetBuilder.Build(Sample(), 2809, Cur(AllJa(), excluded: new() { ["the"] = "機能語" }), Options);

        Assert.Contains(("be", "1〜2文字"), result.Excluded);
        Assert.Contains(("the", "機能語"), result.Excluded);
        Assert.Contains(result.Set.Words, w => w.Word == "tax"); // 3文字の内容語は残す
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Tiers_follow_the_tier1_list_then_nawl_or_bsl_then_others()
    {
        var result = WordSetBuilder.Build(Sample(), 2809, Cur(AllJa(), tier1: ["receive"]), Options);
        int Tier(string word) => result.Set.Words.Single(w => w.Word == word).PriorityTier;

        Assert.Equal(1, Tier("receive"));
        Assert.Equal(2, Tier("analyse"));
        Assert.Equal(2, Tier("invoice"));
        Assert.Equal(3, Tier("committee"));
    }

    [Fact]
    public void Missing_translations_are_an_error_unless_allowed_and_then_those_words_are_left_out()
    {
        var ja = AllJa();
        ja.Remove("invoice");

        var strict = WordSetBuilder.Build(Sample(), 2809, Cur(ja), Options);
        Assert.NotEmpty(strict.Errors);
        Assert.Equal(["invoice"], strict.MissingTranslations);

        var allowed = WordSetBuilder.Build(Sample(), 2809, Cur(ja), Options with { AllowMissingTranslations = true });
        Assert.Empty(allowed.Errors);
        Assert.DoesNotContain(allowed.Set.Words, w => w.Word == "invoice");
    }

    [Fact]
    public void Words_sharing_a_meaning_get_the_initial_and_length_as_a_hint_and_stay_in_japanese_mode()
    {
        var ja = AllJa();
        ja["analyse"] = "受け取る";
        ja["invoice"] = "請求書、受け取る(書類を)"; // 括弧を外した意味単位で重なる

        var result = WordSetBuilder.Build(Sample(), 2809, Cur(ja), Options);
        string Ja(string word) => result.Set.Words.Single(w => w.Word == word).Ja;

        Assert.Equal("受け取る(a・7文字)", Ja("analyse"));
        Assert.Equal("受け取る(r・7文字)", Ja("receive"));
        Assert.Equal("請求書、受け取る(書類を)(i・7文字)", Ja("invoice"));
        Assert.Equal("委員会", Ja("committee")); // 重ならない語には付けない
        Assert.Equal(3, result.HintedCount);
        Assert.Empty(result.SameTranslationGroups);
        Assert.All(result.Set.Words, w => Assert.True(w.JapaneseEligible));
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Words_still_ambiguous_with_the_same_initial_and_length_are_errors()
    {
        var merged = WordMerger.Merge([W("begin", 189), W("break", 300)], [], []);
        var ja = new Dictionary<string, string> { ["begin"] = "始める", ["break"] = "壊す、始める(〜し)" };

        var result = WordSetBuilder.Build(merged, 2809, Cur(ja), Options);

        var group = Assert.Single(result.AmbiguousGroups);
        Assert.Equal("始める", group.Meaning);
        Assert.Equal(["begin", "break"], group.Words);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Curated_words_not_in_the_lists_are_errors_so_typos_are_not_ignored()
    {
        var result = WordSetBuilder.Build(Sample(), 2809, Cur(AllJa(), tier1: ["comittee"]), Options);

        Assert.Equal(["comittee"], result.UnknownCuratedWords);
        Assert.NotEmpty(result.Errors);
    }

    // ---- 書き込むかどうか ----

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)] // 訳がそろっていない途中のデータで同梱のデータを置き換えない
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void Words_json_is_written_only_for_a_complete_error_free_build(bool allowMissing, bool reportOnly, bool expected)
    {
        var ok = WordSetBuilder.Build(Sample(), 2809, Cur(AllJa()), Options);

        Assert.Equal(expected, new RunOptions(allowMissing, reportOnly).ShouldWriteWordsJson(ok));
    }

    [Fact]
    public void Words_json_is_not_written_when_there_are_errors()
    {
        var ja = AllJa();
        ja.Remove("invoice");
        var failed = WordSetBuilder.Build(Sample(), 2809, Cur(ja), Options);

        Assert.False(new RunOptions(false, false).ShouldWriteWordsJson(failed));
    }

    [Fact]
    public void Manual_blanks_win_over_generated_ones_and_invalid_manual_blanks_are_errors()
    {
        var good = WordSetBuilder.Build(Sample(), 2809, Cur(AllJa(), blanks: new() { ["committee"] = "c_mmittee" }), Options);
        var committee = good.Set.Words.Single(w => w.Word == "committee");
        Assert.Equal("c_mmittee", committee.Blanks);
        Assert.Equal(BlankSource.Manual, committee.BlankSource);
        var receive = good.Set.Words.Single(w => w.Word == "receive");
        Assert.Equal(BlankGenerator.Generate("receive").Blanks, receive.Blanks);
        Assert.Equal(BlankSource.Auto, receive.BlankSource);
        Assert.Equal(1, good.BlankRules["Manual"]);

        var bad = WordSetBuilder.Build(Sample(), 2809, Cur(AllJa(), blanks: new() { ["committee"] = "c_mmitte" }), Options);
        Assert.NotEmpty(bad.Errors);
    }

    [Fact]
    public void Zero_words_is_an_error_so_the_bundled_file_is_never_emptied()
    {
        var result = WordSetBuilder.Build(Sample(), 2809, Cur(), Options with { AllowMissingTranslations = true });

        Assert.Empty(result.Set.Words);
        Assert.NotEmpty(result.Errors);
    }


    // ---- 付加データの読込 ----

    [Fact]
    public void Curation_pairs_skip_comments_and_reject_duplicates_or_missing_tabs()
    {
        var map = Curation.ReadPairs([("a.tsv", "# 訳\r\nTax\t税\r\n\r\nlaw\t法律\r\n")]);
        Assert.Equal(new Dictionary<string, string> { ["tax"] = "税", ["law"] = "法律" }, map);

        Assert.Throws<InvalidDataException>(() => Curation.ReadPairs([("a.tsv", "tax\t税\n"), ("b.tsv", "tax\t課税\n")]));
        Assert.Throws<InvalidDataException>(() => Curation.ReadPairs([("a.tsv", "tax 税\n")]));
        Assert.Throws<InvalidDataException>(() => Curation.ReadPairs([("a.tsv", "tax\t \n")]));
        Assert.Throws<InvalidDataException>(() => Curation.ReadPairs([("a.tsv", "tax\t税\t余分\n")])); // 値の中のタブ(制御文字)
        Assert.Throws<InvalidDataException>(() => Curation.ReadPairs([("a.tsv", "tax\t" + new string('税', Curation.MaxValueLength + 1) + "\n")]));
    }

    [Fact]
    public void Tier1_list_with_a_duplicate_word_is_an_error() =>
        Assert.Throws<InvalidDataException>(() => Curation.ReadWords("# Tier 1\ntax\nlaw\nTax\n"));

    // ---- 出力 ----

    [Fact]
    public void Written_words_json_round_trips_through_the_app_loader_and_validates()
    {
        var result = WordSetBuilder.Build(Sample(), 2809, Cur(AllJa(), tier1: ["receive"]), Options);

        var json = Output.WordsJson(result.Set);
        var loaded = WordSetLoader.Parse(json);

        Assert.Contains("委員会", json, StringComparison.Ordinal); // 日本語をそのまま書く
        Assert.Equal(result.Set.Words.Select(w => w.Id), loaded.Words.Select(w => w.Id));
        Assert.Equal(result.Set.Words.Select(w => (w.Ja, w.Blanks, w.PriorityTier, w.FrequencyRank)), loaded.Words.Select(w => (w.Ja, w.Blanks, w.PriorityTier, w.FrequencyRank)));
        Assert.DoesNotContain(WordSetValidator.Validate(loaded), i => i.Severity == ValidationSeverity.Error);
        Assert.Equal(json, Output.WordsJson(WordSetBuilder.Build(Sample(), 2809, Cur(AllJa(), tier1: ["receive"]), Options).Set)); // 同じ入力 → 同じ出力
    }

    [Fact]
    public void Report_lists_counts_missing_translations_and_exclusions()
    {
        var ja = AllJa();
        ja.Remove("invoice");
        var result = WordSetBuilder.Build(Sample(), 2809, Cur(ja, excluded: new() { ["the"] = "機能語" }), Options with { AllowMissingTranslations = true });

        var report = Output.Report(result, new Dictionary<string, int> { ["NGSL"] = 5 }, Sample().Words.Count);

        Assert.Contains("## 訳がまだ無い語: 1", report, StringComparison.Ordinal);
        Assert.Contains("invoice", report, StringComparison.Ordinal);
        Assert.Contains("機能語(1): the", report, StringComparison.Ordinal);
        Assert.Contains("## 検証エラー: 0", report, StringComparison.Ordinal);
    }
}
