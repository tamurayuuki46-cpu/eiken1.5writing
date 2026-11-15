using System.Collections.Immutable;

using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Core.Tests;

/// <summary>出題の選択を語彙セットの範囲に限定する(P0-AC v0.2 C 節、ST-04/05/06/10、AC-X02/14)。</summary>
public class QuestionSelectorScopeTests
{
    private static IReadOnlySet<string> Scope(params IEnumerable<string>[] groups) =>
        groups.SelectMany(g => g).ToHashSet(StringComparer.Ordinal);

    private static SelectionResult Run(WordSet set, IReadOnlySet<string>? scope, LearningProgress? p = null, int count = 10, int seed = 1,
        bool weakOnly = false, IReadOnlySet<LearningMode>? modes = null, string[]? sources = null) =>
        QuestionSelector.Select(
            set, p ?? LearningProgress.Empty, Sel.Settings(count, weakOnly, sources: sources), modes ?? Sel.AllModes, Sel.Now, new Random(seed), scope);

    [Fact]
    public void ST04_due_review_and_high_priority_weak_outside_the_scope_never_enter_the_plan()
    {
        var inside = Sel.Many("in", 8);
        var outsideWeak = Sel.W("out-weak", tier: 1);
        var outsideReview = Sel.W("out-review", tier: 1);
        var set = Sel.Set(inside, [outsideWeak, outsideReview]);
        var progress = Sel.P(
            [Sel.Weak("out-weak", mistakes: 5, lastWrong: Sel.Now.AddHours(-1))],
            [Sel.Mastered("out-review", Sel.Today.AddDays(-3))]);

        var result = Run(set, Scope(inside.Select(w => w.Id)), progress, count: 20);

        Assert.Equal(8, result.Plan.Length);
        Assert.All(result.Ids(), id => Assert.StartsWith("in", id));
        Assert.Equal((8, 0, 0, 0), result.Counts());
    }

    [Fact]
    public void ST04_inside_review_weak_unseen_and_seen_are_all_selected_within_the_scope()
    {
        var w = Sel.Many("w", 10);
        var set = Sel.Set(w);
        var progress = Sel.P(
            [Sel.Mastered("w000", Sel.Today.AddDays(-1))],
            [Sel.Weak("w001")],
            [Sel.Normal("w002", Sel.Now.AddDays(-2))]);
        var scope = Scope(w.Take(6).Select(x => x.Id)); // w000〜w005

        var result = Run(set, scope, progress, count: 20);

        Assert.Equal(6, result.Plan.Length);
        Assert.All(result.Ids(), id => Assert.Contains(id, scope));
        Assert.Equal((3, 1, 1, 1), result.Counts()); // 未出題 w003〜5 / 既出 w002 / 苦手 w001 / 再確認 w000
    }

    [Fact]
    public void ST04_weak_only_inside_the_scope_ignores_weak_words_outside()
    {
        var set = Sel.Set(Sel.Many("w", 6));
        var progress = Sel.P([Sel.Weak("w000", mistakes: 5)], [Sel.Weak("w004")]);

        var result = Run(set, Scope(["w003", "w004", "w005"]), progress, weakOnly: true);

        Assert.Equal(["w004"], result.Ids().ToArray());
    }

    [Fact]
    public void ST05_no_scope_matches_the_unscoped_overload_for_the_same_clock_and_random()
    {
        var w = Sel.Many("w", 40);
        var set = Sel.Set(w);
        var progress = Sel.P(
            [Sel.Weak("w001", 3)], [Sel.Weak("w002")], [Sel.Mastered("w003", Sel.Today.AddDays(-1))], [Sel.Normal("w004", Sel.Now.AddDays(-3))]);

        var scoped = Run(set, scope: null, progress, count: 10, seed: 42);
        var legacy = QuestionSelector.Select(set, progress, Sel.Settings(10), Sel.AllModes, Sel.Now, new Random(42));

        Assert.Equal(legacy.Plan.Select(q => (q.WordId, q.Mode)).ToArray(), scoped.Plan.Select(q => (q.WordId, q.Mode)).ToArray());
        Assert.Equal(legacy.Counts(), scoped.Counts());
    }

    [Fact]
    public void ST05_a_scope_holding_every_word_gives_the_same_plan_as_no_scope()
    {
        var w = Sel.Many("w", 40);
        var set = Sel.Set(w);
        var all = Scope(w.Select(x => x.Id));

        var scoped = Run(set, all, count: 10, seed: 7);
        var none = Run(set, null, count: 10, seed: 7);

        Assert.Equal(none.Plan.Select(q => (q.WordId, q.Mode)).ToArray(), scoped.Plan.Select(q => (q.WordId, q.Mode)).ToArray());
    }

    [Fact]
    public void ST10_an_empty_scope_is_not_the_same_as_no_scope()
    {
        var set = Sel.Set(Sel.Many("w", 10));

        var empty = Run(set, Scope());
        var unscoped = Run(set, null);

        Assert.Empty(empty.Plan);
        Assert.Equal(SelectionEmptyReason.EmptyScope, empty.EmptyReason);
        Assert.NotEmpty(unscoped.Plan);
    }

    [Fact]
    public void A_scope_of_ids_that_are_not_in_the_word_data_selects_nothing()
    {
        var result = Run(Sel.Set(Sel.Many("w", 10)), Scope(["ghost1", "ghost2"]));

        Assert.Empty(result.Plan);
        Assert.NotEqual(SelectionEmptyReason.None, result.EmptyReason);
    }

    [Fact]
    public void ST10_mode_ineligible_words_inside_the_scope_are_not_planned()
    {
        // 穴あき不可・日本語不可・音声不可・フラッシュは常に可。手動モード穴あきなら Blanks の無い語は出ない
        var set = Sel.Set([Sel.W("with-blank"), Sel.W("no-blank", blank: false), Sel.W("also-blank")]);
        var settings = Sel.Settings(10, autoMix: false, manual: Blank);

        var result = QuestionSelector.Select(
            set, LearningProgress.Empty, settings, Sel.AllModes, Sel.Now, new Random(1), Scope(["with-blank", "no-blank"]));

        Assert.Equal(["with-blank"], result.Ids().ToArray());
    }

    [Fact]
    public void ST10_audio_unavailable_modes_are_respected_inside_the_scope()
    {
        var set = Sel.Set([Sel.W("a1", ja: false), Sel.W("b2")]);
        var settings = Sel.Settings(10, autoMix: false, manual: Audio);

        var result = QuestionSelector.Select(
            set, LearningProgress.Empty, settings, Sel.NoAudio, Sel.Now, new Random(1), Scope(["a1", "b2"]));

        Assert.Empty(result.Plan);
        Assert.Equal(SelectionEmptyReason.ManualModeUnavailable, result.EmptyReason);
    }

    [Fact]
    public void Enabled_sources_still_limit_new_words_inside_the_scope_but_not_weak_ones()
    {
        var set = Sel.Set([
            Sel.W("ngsl-new", sources: "NGSL"),
            Sel.W("bsl-new", sources: "BSL"),
            Sel.W("bsl-weak", sources: "BSL"),
        ]);
        var progress = Sel.P([Sel.Weak("bsl-weak")]);

        var result = Run(set, Scope(["ngsl-new", "bsl-new", "bsl-weak"]), progress, sources: ["NGSL"]);

        Assert.Equal(["bsl-weak", "ngsl-new"], result.Ids().Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void ST06_fewer_eligible_words_than_requested_give_a_short_plan_without_error()
    {
        var w = Sel.Many("w", 30);

        var result = Run(Sel.Set(w), Scope(w.Take(8).Select(x => x.Id)), count: 20);

        Assert.Equal(8, result.Plan.Length);
        Assert.Equal(SelectionEmptyReason.None, result.EmptyReason);
    }

    [Fact]
    public void New_words_are_picked_in_tier_then_frequency_order_inside_the_scope_before_shuffling()
    {
        var set = Sel.Set([
            Sel.W("t1", tier: 1, freq: 1), Sel.W("t2", tier: 2, freq: 1), Sel.W("t3a", tier: 3, freq: 1), Sel.W("t3b", tier: 3, freq: 2),
            Sel.W("out", tier: 1, freq: 1),
        ]);
        var scope = Scope(["t1", "t2", "t3a", "t3b"]);

        var result = Run(set, scope, count: 3, weakOnly: false);

        Assert.Equal(3, result.Plan.Length);
        Assert.DoesNotContain("out", result.Ids());
        Assert.DoesNotContain("t3b", result.Ids());
    }

    [Fact]
    public void The_selector_does_not_mutate_the_scope_or_the_progress()
    {
        var w = Sel.Many("w", 12);
        var progress = Sel.P([Sel.Weak("w001")]);
        var scope = Scope(w.Take(6).Select(x => x.Id));

        _ = Run(Sel.Set(w), scope, progress);

        Assert.Equal(6, scope.Count);
        Assert.Single(progress.Words);
    }
}
