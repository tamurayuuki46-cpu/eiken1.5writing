using System.Collections.Immutable;

using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;

namespace EikenSpelling.Core.Tests;

/// <summary>語彙セットの整列・分割・単位切替(UI v0.3 S-06、UC-07/08、ST-01〜03/09/10)。</summary>
public class VocabularySetTests
{
    /// <summary>実データに近い 4,924 語(Tier 1〜3、頻度順位あり・なしを混ぜる)。ID は固定長で並べ替えの結果を確かめやすくする。</summary>
    internal static WordSet Big(int count = 4924) =>
        Sel.Set(Enumerable.Range(0, count).Select(i => Sel.W(
            $"w{i:D4}",
            tier: 1 + (i * 7 % 3),
            freq: i % 5 == 0 ? null : (i * 31) % 997 + 1)));

    private static string[] Ids(VocabularySet set) => [.. set.Words.Select(w => w.Id)];

    [Fact]
    public void ST02_order_is_tier_then_ranked_first_then_rank_then_ordinal_id()
    {
        var set = Sel.Set([
            Sel.W("zeta", tier: 2, freq: 1),
            Sel.W("Bravo", tier: 1, freq: 5),      // 大文字は Ordinal で小文字より前
            Sel.W("alpha", tier: 1, freq: 5),
            Sel.W("unranked-b", tier: 1),
            Sel.W("unranked-a", tier: 1),
            Sel.W("fast", tier: 1, freq: 2),
            Sel.W("tier3", tier: 3, freq: 1),
        ]);

        var ordered = VocabularySets.Order(set).Select(w => w.Id).ToArray();

        Assert.Equal(["fast", "Bravo", "alpha", "unranked-a", "unranked-b", "zeta", "tier3"], ordered);
    }

    [Fact]
    public void ST02_order_does_not_depend_on_input_order()
    {
        var words = Big(300).Words.ToList();
        var shuffled = Sel.Set(words.OrderBy(w => w.Id.GetHashCode() ^ 12345).ToList());

        Assert.Equal(
            VocabularySets.Order(Big(300)).Select(w => w.Id).ToArray(),
            VocabularySets.Order(shuffled).Select(w => w.Id).ToArray());
    }

    [Fact]
    public void Order_skips_blank_ids_and_keeps_only_the_first_of_duplicate_ids()
    {
        var first = Sel.W("dup", tier: 1);
        var second = Sel.W("dup", tier: 3);
        var set = Sel.Set([first, second, Sel.W("ok", tier: 2)]);

        var ordered = VocabularySets.Order(set);

        Assert.Equal(["dup", "ok"], ordered.Select(w => w.Id).ToArray());
        Assert.Equal(1, ordered[0].PriorityTier);
    }

    [Fact]
    public void ST01_ST03_full_data_has_99_fifty_sets_and_50_hundred_sets_with_24_in_the_last()
    {
        var ordered = VocabularySets.Order(Big());

        Assert.Equal(4924, ordered.Length);
        Assert.Equal(99, VocabularySets.SetCount(ordered.Length, 50));
        Assert.Equal(50, VocabularySets.SetCount(ordered.Length, 100));
        Assert.Equal(24, VocabularySets.Get(ordered, 50, 99).Words.Length);
        Assert.Equal(24, VocabularySets.Get(ordered, 100, 50).Words.Length);
        Assert.Equal(50, VocabularySets.Get(ordered, 50, 98).Words.Length);
        Assert.Equal(100, VocabularySets.Get(ordered, 100, 49).Words.Length);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    public void ST01_every_word_id_appears_exactly_once_across_the_sets(int unit)
    {
        var ordered = VocabularySets.Order(Big());
        var total = VocabularySets.SetCount(ordered.Length, unit);

        var all = Enumerable.Range(1, total).SelectMany(n => Ids(VocabularySets.Get(ordered, unit, n))).ToList();

        Assert.Equal(ordered.Select(w => w.Id).ToArray(), all.ToArray());
        Assert.Equal(all.Count, all.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ST03_hundred_set_is_two_adjacent_fifty_sets_in_the_same_order()
    {
        var ordered = VocabularySets.Order(Big());

        for (var n = 1; n <= 50; n++)
        {
            var pair = Ids(VocabularySets.Get(ordered, 50, 2 * n - 1)).Concat(n * 2 <= 99 ? Ids(VocabularySets.Get(ordered, 50, 2 * n)) : []);
            Assert.Equal(pair.ToArray(), Ids(VocabularySets.Get(ordered, 100, n)));
        }
    }

    [Fact]
    public void Set_pages_know_their_number_total_and_unit()
    {
        var ordered = VocabularySets.Order(Big());

        var page = VocabularySets.Get(ordered, 50, 7);

        Assert.Equal((50, 7, 99), (page.UnitSize, page.Number, page.Total));
    }

    [Fact]
    public void Exact_multiple_has_no_empty_tail_set_and_empty_data_has_no_sets()
    {
        Assert.Equal(2, VocabularySets.SetCount(100, 50));
        Assert.Equal(1, VocabularySets.SetCount(100, 100));
        Assert.Equal(1, VocabularySets.SetCount(1, 50));
        Assert.Equal(0, VocabularySets.SetCount(0, 50));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(51)]
    [InlineData(200)]
    public void Unsupported_units_are_rejected(int unit)
    {
        var ordered = VocabularySets.Order(Big(120));

        Assert.False(VocabularySets.IsSupportedUnit(unit));
        Assert.Throws<ArgumentOutOfRangeException>(() => VocabularySets.SetCount(120, unit));
        Assert.Throws<ArgumentOutOfRangeException>(() => VocabularySets.Get(ordered, unit, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    public void Out_of_range_set_numbers_are_rejected(int number)
    {
        var ordered = VocabularySets.Order(Big(150)); // 50語は3セット

        Assert.Throws<ArgumentOutOfRangeException>(() => VocabularySets.Get(ordered, 50, number));
    }

    [Fact]
    public void ST03_unit_switch_keeps_the_page_containing_the_original_first_word()
    {
        var ordered = VocabularySets.Order(Big());
        var fifty3 = VocabularySets.Get(ordered, 50, 3); // 100〜149番目
        var fifty4 = VocabularySets.Get(ordered, 50, 4); // 150〜199番目

        Assert.Equal(2, VocabularySets.NumberContaining(ordered, 100, fifty3.Words[0].Id));
        Assert.Equal(2, VocabularySets.NumberContaining(ordered, 100, fifty4.Words[0].Id));
        // 100語 → 50語: 100語の2ページ目の先頭(100番目)を含む50語は3ページ目
        var hundred2 = VocabularySets.Get(ordered, 100, 2);
        Assert.Equal(3, VocabularySets.NumberContaining(ordered, 50, hundred2.Words[0].Id));
    }

    [Fact]
    public void Number_containing_an_unknown_word_falls_back_to_the_first_set()
    {
        var ordered = VocabularySets.Order(Big(150));

        Assert.Equal(1, VocabularySets.NumberContaining(ordered, 50, "no-such-word"));
    }

    [Fact]
    public void Number_containing_the_last_word_is_the_last_set()
    {
        var ordered = VocabularySets.Order(Big());

        Assert.Equal(99, VocabularySets.NumberContaining(ordered, 50, ordered[^1].Id));
        Assert.Equal(50, VocabularySets.NumberContaining(ordered, 100, ordered[^1].Id));
    }

    [Fact]
    public void ST09_rows_classify_each_word_like_the_home_status()
    {
        var today = Sel.Today;
        var set = Sel.Set([Sel.W("a1", 1, 1), Sel.W("b2", 1, 2), Sel.W("c3", 1, 3), Sel.W("d4", 1, 4), Sel.W("e5", 1, 5)]);
        var progress = Sel.P(
            [Sel.Normal("b2", Sel.Now)],
            [Sel.Weak("c3")],
            [Sel.Mastered("d4", today.AddDays(5))],
            [Sel.Mastered("e5", today)]);
        var ordered = VocabularySets.Order(set);

        var rows = VocabularySets.Rows(VocabularySets.Get(ordered, 50, 1), progress, today);

        Assert.Equal(
            [WordStudyState.Unseen, WordStudyState.Normal, WordStudyState.Weak, WordStudyState.Mastered, WordStudyState.ReviewDue],
            rows.Select(r => r.State).ToArray());
        Assert.Equal("a1", rows[0].WordId);
        Assert.Equal("訳", rows[0].Japanese);
        Assert.Equal(1, rows[0].Tier);
    }

    [Fact]
    public void Rows_are_a_pure_read_and_do_not_change_progress()
    {
        var progress = Sel.P([Sel.Weak("w0001")]);
        var ordered = VocabularySets.Order(Big(60));

        _ = VocabularySets.Rows(VocabularySets.Get(ordered, 50, 1), progress, Sel.Today);

        Assert.Single(progress.Words);
    }

    [Fact]
    public void Scope_from_a_set_keeps_the_display_order_and_versions()
    {
        var ordered = VocabularySets.Order(Big(120));
        var page = VocabularySets.Get(ordered, 50, 2);

        var scope = VocabularySetScope.From(page, "v9");

        Assert.Equal((50, 2, 3, "v9"), (scope.UnitSize, scope.Number, scope.TotalSets, scope.WordSetVersion));
        Assert.Equal(Ids(page), scope.WordIds.ToArray());
        Assert.Null(scope.Problem());
    }

    [Fact]
    public void Scope_problems_are_detected()
    {
        var ok = new VocabularySetScope(50, 1, 3, "v", ["a", "b"]);

        Assert.Null(ok.Problem());
        Assert.NotNull((ok with { UnitSize = 70 }).Problem());
        Assert.NotNull((ok with { Number = 0 }).Problem());
        Assert.NotNull((ok with { Number = 4 }).Problem());
        Assert.NotNull((ok with { TotalSets = 0 }).Problem());
        Assert.NotNull((ok with { WordIds = [] }).Problem());
        Assert.NotNull((ok with { WordIds = default }).Problem());
        Assert.NotNull((ok with { WordIds = ["a", "a"] }).Problem());
        Assert.NotNull((ok with { WordIds = ["a", " "] }).Problem());
        Assert.NotNull((ok with { WordIds = [.. Enumerable.Range(0, 51).Select(i => $"w{i}")] }).Problem());
        Assert.NotNull((ok with { WordSetVersion = null! }).Problem());
    }
}
