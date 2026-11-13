using System.Collections.Immutable;
using System.Diagnostics;

using EikenSpelling.Core.Application;
using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

using Xunit.Abstractions;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Core.Tests;

/// <summary>出題選択のテスト用の道具。今日は 2026-12-01(日本時間)10:00。</summary>
internal static class Sel
{
    public static readonly DateOnly Today = new(2026, 12, 1);
    public static readonly DateTimeOffset Now = Jst.At(Today, 10);
    public static readonly IReadOnlySet<LearningMode> AllModes = new HashSet<LearningMode> { Japanese, Audio, Blank, Flash };
    public static readonly IReadOnlySet<LearningMode> NoAudio = new HashSet<LearningMode> { Japanese, Blank, Flash };

    public static WordEntry W(string id, int tier = 3, int? freq = null, bool ja = true, bool audio = true, bool blank = true, params string[] sources) =>
        new()
        {
            Id = id, Word = id, Ja = "訳", Blanks = blank ? "_" + id[1..] : null, PriorityTier = tier, FrequencyRank = freq,
            JapaneseEligible = ja, AudioEligible = audio, Sources = sources.Length == 0 ? ["NGSL"] : sources,
        };

    public static WordEntry[] Many(string prefix, int n) => Enumerable.Range(0, n).Select(i => W($"{prefix}{i:D3}")).ToArray();

    public static WordSet Set(params IEnumerable<WordEntry>[] groups) => new() { Words = groups.SelectMany(g => g).ToList() };

    public static WordProgress Weak(string id, int mistakes = 1, DateTimeOffset? lastWrong = null, DateTimeOffset? lastAsked = null, int streak = 0, DateOnly? eligible = null) =>
        new()
        {
            WordId = id, State = LearningState.Weak, MasteryStreak = streak, LastEligibleCorrectDate = streak > 0 ? eligible ?? Today.AddDays(-1) : null,
            LastNormalAskedAt = lastAsked ?? Now.AddDays(-1),
            Modes = ImmutableDictionary<LearningMode, ModeRecord>.Empty.Add(Japanese, new ModeRecord { NormalWrong = mistakes, LastWrongAt = lastWrong ?? Now.AddDays(-30) }),
        };

    public static WordProgress Normal(string id, DateTimeOffset lastAsked) =>
        new() { WordId = id, State = LearningState.Normal, LastNormalAskedAt = lastAsked };

    public static WordProgress Mastered(string id, DateOnly? next) =>
        new()
        {
            WordId = id, State = LearningState.Mastered, MasteryStreak = 2, ReviewStage = 1, NextReviewDate = next,
            LastEligibleCorrectDate = Today.AddDays(-10), MasteredAt = Now.AddDays(-10), LastNormalAskedAt = Now.AddDays(-10),
        };

    public static LearningProgress P(params IEnumerable<WordProgress>[] groups) =>
        LearningProgress.Empty with { Words = groups.SelectMany(g => g).ToImmutableDictionary(w => w.WordId, StringComparer.Ordinal) };

    public static LearningSettings Settings(int count = 10, bool weakOnly = false, bool autoMix = true, LearningMode manual = Japanese, string[]? sources = null) =>
        DefaultSettings.Create() with
        {
            Practice = DefaultSettings.Create().Practice with { QuestionCount = count, WeakWordsOnly = weakOnly, AutoMix = autoMix, ManualMode = manual },
            Vocabulary = sources is null ? DefaultSettings.Create().Vocabulary : DefaultSettings.Create().Vocabulary with { EnabledSources = sources },
        };

    public static SelectionResult Run(WordSet set, LearningProgress? p = null, LearningSettings? s = null, IReadOnlySet<LearningMode>? modes = null, int seed = 1) =>
        QuestionSelector.Select(set, p ?? LearningProgress.Empty, s ?? Settings(), modes ?? AllModes, Now, new Random(seed));

    public static IEnumerable<string> Ids(this SelectionResult r) => r.Plan.Select(q => q.WordId);

    public static (int Unseen, int Seen, int Weak, int Review) Counts(this SelectionResult r) => (r.Unseen, r.Seen, r.Weak, r.Review);
}

public class WeakWordScorerTests
{
    private static long Score(WordProgress w, LearningSettings? s = null, DateTimeOffset? now = null) =>
        WeakWordScorer.Score(w, s ?? DefaultSettings.Create(), now ?? Sel.Now);

    // 既定: 上限5、直近7日 +3、あと1回 +2、14日以上 +2。基準の単語は直近でなく(30日前)、最終出題は昨日
    [Theory]
    [InlineData(4, 4)]
    [InlineData(5, 5)]
    [InlineData(6, 5)]
    public void Mistakes_are_capped(int mistakes, long expected)
    {
        Assert.Equal(expected, Score(Sel.Weak("a", mistakes)));
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(6, 4)]
    [InlineData(7, 1)]
    public void Recent_mistake_within_seven_days_including_today(int daysAgo, long expected)
    {
        Assert.Equal(expected, Score(Sel.Weak("a", lastWrong: Jst.At(Sel.Today.AddDays(-daysAgo), 9))));
    }

    [Fact]
    public void Recent_mistake_uses_jst_midnight()
    {
        var w = Sel.Weak("a", lastWrong: Jst.At(Sel.Today.AddDays(-6), 12));
        var lastSecondOfDay = new DateTimeOffset(Sel.Today.Year, Sel.Today.Month, Sel.Today.Day, 14, 59, 59, TimeSpan.Zero); // JST 12/1 23:59:59
        var nextMidnight = lastSecondOfDay.AddSeconds(1);                                                                    // JST 12/2 0:00:00

        Assert.Equal(4, Score(w, now: lastSecondOfDay));
        Assert.Equal(1, Score(w, now: nextMidnight));
    }

    [Theory]
    [InlineData(2, 1, 3)] // あと1回 → +2
    [InlineData(2, 0, 1)]
    [InlineData(1, 0, 1)] // 必要回数1では「あと1回」の状態が無い
    public void Pending_mastery_bonus(int required, int streak, long expected)
    {
        var s = DefaultSettings.Create() with { Mastery = DefaultSettings.Create().Mastery with { RequiredCorrectDays = required } };

        Assert.Equal(expected, Score(Sel.Weak("a", streak: streak), s));
    }

    [Fact]
    public void Pending_bonus_is_not_given_if_already_correct_today()
    {
        Assert.Equal(1, Score(Sel.Weak("a", streak: 1, eligible: Sel.Today)));
    }

    [Theory]
    [InlineData(13, 1)]
    [InlineData(14, 3)]
    [InlineData(15, 3)]
    public void Overdue_bonus_after_fourteen_days(int daysAgo, long expected)
    {
        Assert.Equal(expected, Score(Sel.Weak("a", lastAsked: Jst.At(Sel.Today.AddDays(-daysAgo), 9))));
    }

    [Fact]
    public void Missing_dates_and_extreme_dates_do_not_throw()
    {
        var noAsked = Sel.Weak("a") with { LastNormalAskedAt = null };
        var noWrong = Sel.Weak("a") with { Modes = ImmutableDictionary<LearningMode, ModeRecord>.Empty.Add(Japanese, new ModeRecord { NormalWrong = 1 }) };
        var extreme = Sel.Weak("a", lastWrong: DateTimeOffset.MaxValue, lastAsked: DateTimeOffset.MaxValue);

        Assert.Equal(3, Score(noAsked));  // 最終出題なし → 経過の加点
        Assert.Equal(1, Score(noWrong));  // 誤答日時なし → 直近の加点なし
        Assert.Equal(4, Score(extreme));  // 未来・極端な日時は今日扱い(直近 +3、経過なし)
    }

    [Fact]
    public void Values_come_from_settings()
    {
        var s = DefaultSettings.Create() with
        {
            WeakWordScore = DefaultSettings.Create().WeakWordScore with { MistakeCap = 2, RecentMistakeDays = 1, RecentMistakeBonus = 10 },
        };

        Assert.Equal(2, Score(Sel.Weak("a", 9, lastWrong: Jst.At(Sel.Today.AddDays(-1), 9)), s)); // 1日前は RecentMistakeDays=1 の外
        Assert.Equal(12, Score(Sel.Weak("a", 9, lastWrong: Sel.Now), s));
    }

    [Fact]
    public void Order_by_score_then_today_then_oldest_asked_then_id()
    {
        var high = Sel.Weak("z", 5);
        var todayDone = Sel.Weak("b", 2, streak: 1, eligible: Sel.Today, lastAsked: Sel.Now.AddDays(-5)); // 2(あと1回の加点なし)
        var oldAsked = Sel.Weak("c", 2, lastAsked: Sel.Now.AddDays(-5));
        var newAsked = Sel.Weak("d", 2, lastAsked: Sel.Now.AddDays(-2));
        var neverAsked = Sel.Weak("e", 0) with { LastNormalAskedAt = null };   // 0 + 経過 +2 = 2
        var sameAsE = Sel.Weak("a", 0) with { LastNormalAskedAt = null };

        var order = WeakWordScorer.Order([newAsked, todayDone, high, oldAsked, neverAsked, sameAsE], DefaultSettings.Create(), Sel.Now);

        Assert.Equal(["z", "a", "e", "c", "d", "b"], order.Select(w => w.WordId));
    }
}

public class ModeEligibilityTests
{
    [Fact]
    public void All_modes_in_fixed_order()
    {
        Assert.Equal([Japanese, Audio, Blank, Flash], ModeEligibility.For(Sel.W("abc"), Sel.AllModes).ToArray());
    }

    [Fact]
    public void Word_flags_and_blank_text_are_respected()
    {
        Assert.Equal([Audio, Blank, Flash], ModeEligibility.For(Sel.W("abc", ja: false), Sel.AllModes).ToArray());
        Assert.Equal([Audio, Blank, Flash], ModeEligibility.For(Sel.W("abc") with { Ja = "  " }, Sel.AllModes).ToArray());
        Assert.Equal([Japanese, Audio, Flash], ModeEligibility.For(Sel.W("abc") with { Blanks = " " }, Sel.AllModes).ToArray());
        Assert.Equal([Japanese, Blank, Flash], ModeEligibility.For(Sel.W("abc", audio: false), Sel.AllModes).ToArray());
    }

    [Fact]
    public void Device_modes_are_intersected()
    {
        Assert.Equal([Japanese, Blank, Flash], ModeEligibility.For(Sel.W("abc"), Sel.NoAudio).ToArray());
    }

    [Fact]
    public void Word_without_mastery_mode_is_not_playable()
    {
        Assert.Empty(ModeEligibility.For(Sel.W("abc", ja: false, audio: false), Sel.AllModes));
        Assert.Empty(ModeEligibility.For(Sel.W("abc", ja: false), Sel.NoAudio)); // 音声の無い端末で日本語不可
    }
}

public class QuestionSelectorSlotTests
{
    [Fact]
    public void Only_unseen_words_fill_all_ten()
    {
        var r = Sel.Run(Sel.Set(Sel.Many("u", 20)));

        Assert.Equal((10, 0, 0, 0), r.Counts());
        Assert.Equal(SelectionEmptyReason.None, r.EmptyReason);
    }

    [Fact]
    public void Weak_and_unseen_are_five_and_five()
    {
        var weak = Sel.Many("k", 10);
        var r = Sel.Run(Sel.Set(Sel.Many("u", 10), weak), Sel.P(weak.Select(w => Sel.Weak(w.Id))));

        Assert.Equal((5, 0, 5, 0), r.Counts());
    }

    [Fact]
    public void Due_review_takes_one_weak_slot()
    {
        var weak = Sel.Many("k", 10);
        var m = Sel.W("m01");
        var r = Sel.Run(Sel.Set(Sel.Many("u", 10), weak, [m]), Sel.P(weak.Select(w => Sel.Weak(w.Id)), [Sel.Mastered("m01", Sel.Today)]));

        Assert.Equal((5, 0, 4, 1), r.Counts());
    }

    [Theory]
    [InlineData(5, 3, 1, 1)]
    [InlineData(6, 3, 2, 1)]
    [InlineData(10, 5, 4, 1)]
    [InlineData(11, 6, 3, 2)]
    [InlineData(20, 10, 8, 2)]
    [InlineData(21, 11, 8, 2)]
    public void Review_limit_follows_question_count(int count, int unseen, int weak, int review)
    {
        var weakWords = Sel.Many("k", 20);
        var mastered = Sel.Many("m", 5);
        var r = Sel.Run(
            Sel.Set(Sel.Many("u", 30), weakWords, mastered),
            Sel.P(weakWords.Select(w => Sel.Weak(w.Id)), mastered.Select(w => Sel.Mastered(w.Id, Sel.Today))),
            Sel.Settings(count));

        Assert.Equal((unseen, 0, weak, review), r.Counts());
    }

    [Fact]
    public void Review_uses_oldest_due_date_then_id_and_overflow_does_not_go_to_weak()
    {
        var weakWords = Sel.Many("k", 10);
        var mastered = new[] { Sel.W("m3"), Sel.W("m1"), Sel.W("m2") };
        var p = Sel.P(weakWords.Select(w => Sel.Weak(w.Id)),
            [Sel.Mastered("m3", Sel.Today.AddDays(-5)), Sel.Mastered("m1", Sel.Today), Sel.Mastered("m2", Sel.Today.AddDays(-5))]);

        var r = Sel.Run(Sel.Set(Sel.Many("u", 10), weakWords, mastered), p);

        Assert.Equal((5, 0, 4, 1), r.Counts());
        Assert.Contains("m2", r.Ids());
        Assert.DoesNotContain("m3", r.Ids());
        Assert.DoesNotContain("m1", r.Ids());
    }

    [Fact]
    public void Review_limit_zero_selects_no_review()
    {
        var weakWords = Sel.Many("k", 10);
        var s = Sel.Settings() with { Mastery = DefaultSettings.Create().Mastery with { ReviewLimits = [new(100, 0)] } };
        var r = Sel.Run(Sel.Set(Sel.Many("u", 10), weakWords, [Sel.W("m01")]),
            Sel.P(weakWords.Select(w => Sel.Weak(w.Id)), [Sel.Mastered("m01", Sel.Today)]), s);

        Assert.Equal((5, 0, 5, 0), r.Counts());
    }

    [Fact]
    public void Weak_shortage_is_filled_with_new()
    {
        var weak = Sel.Many("k", 2);
        var r = Sel.Run(Sel.Set(Sel.Many("u", 20), weak), Sel.P(weak.Select(w => Sel.Weak(w.Id))));

        Assert.Equal((8, 0, 2, 0), r.Counts());
    }

    [Fact]
    public void Weak_only_does_not_fill_and_still_includes_due_review()
    {
        var weak = Sel.Many("k", 3);
        var s = Sel.Settings(weakOnly: true);

        Assert.Equal((0, 0, 3, 0), Sel.Run(Sel.Set(Sel.Many("u", 20), weak), Sel.P(weak.Select(w => Sel.Weak(w.Id))), s).Counts());
        var reviewOnly = Sel.Run(Sel.Set(Sel.Many("u", 20), [Sel.W("m01")]), Sel.P([Sel.Mastered("m01", Sel.Today)]), s);
        Assert.Equal((0, 0, 0, 1), reviewOnly.Counts());
        Assert.Equal(SelectionEmptyReason.None, reviewOnly.EmptyReason);
    }

    [Theory]
    [InlineData(4, 4, 1, 5)] // 未出題が 枠−1 → 既出で1つ補う
    [InlineData(5, 5, 0, 5)] // ちょうど
    [InlineData(0, 0, 5, 5)] // 未出題なし → 既出
    public void Unseen_then_seen_fill_new_slots(int unseenCount, int unseen, int seen, int weak)
    {
        var weakWords = Sel.Many("k", 10);
        var seenWords = Sel.Many("s", 10);
        var p = Sel.P(weakWords.Select(w => Sel.Weak(w.Id)), seenWords.Select((w, i) => Sel.Normal(w.Id, Sel.Now.AddDays(-1 - i))));

        var r = Sel.Run(Sel.Set(Sel.Many("u", unseenCount), seenWords, weakWords), p);

        Assert.Equal((unseen, seen, weak, 0), r.Counts());
    }

    [Fact]
    public void Seen_words_are_oldest_first_then_weak_fills_the_rest()
    {
        var weakWords = Sel.Many("k", 10);
        var p = Sel.P(weakWords.Select(w => Sel.Weak(w.Id)), [Sel.Normal("s1", Sel.Now.AddDays(-1)), Sel.Normal("s2", Sel.Now.AddDays(-9))]);

        var r = Sel.Run(Sel.Set([Sel.W("s1"), Sel.W("s2")], weakWords), p, Sel.Settings(count: 6)); // 新規3・苦手3

        Assert.Equal((0, 2, 4, 0), r.Counts());
    }

    [Fact]
    public void Everything_short_returns_fewer_questions()
    {
        var p = Sel.P([Sel.Weak("k1"), Sel.Normal("s1", Sel.Now.AddDays(-2)), Sel.Mastered("m1", Sel.Today)]);

        var r = Sel.Run(Sel.Set([Sel.W("k1"), Sel.W("s1"), Sel.W("m1"), Sel.W("u1")]), p);

        Assert.Equal((1, 1, 1, 1), r.Counts());
        Assert.Equal(4, r.Plan.Length);
    }

    [Fact]
    public void Mastered_words_before_due_or_without_next_date_are_never_selected()
    {
        var p = Sel.P([Sel.Mastered("m1", Sel.Today.AddDays(1)), Sel.Mastered("m2", null)]);

        var r = Sel.Run(Sel.Set([Sel.W("m1"), Sel.W("m2")]), p);

        Assert.Empty(r.Plan);
        Assert.Equal(SelectionEmptyReason.NothingToPractice, r.EmptyReason);
    }
}

public class QuestionSelectorFilterTests
{
    [Fact]
    public void Seen_words_pick_oldest_asked_then_id()
    {
        var weakWords = Sel.Many("k", 10);
        var p = Sel.P(weakWords.Select(w => Sel.Weak(w.Id)),
            [Sel.Normal("sb", Sel.Now.AddDays(-9)), Sel.Normal("sa", Sel.Now.AddDays(-9)), Sel.Normal("sc", Sel.Now.AddDays(-1))]);

        // 6問 = 新規3・苦手3。未出題2語のあと、既出から1語だけ
        var r = Sel.Run(Sel.Set(Sel.Many("u", 2), [Sel.W("sb"), Sel.W("sa"), Sel.W("sc")], weakWords), p, Sel.Settings(count: 6));

        Assert.Contains("sa", r.Ids());
        Assert.DoesNotContain("sb", r.Ids());
        Assert.DoesNotContain("sc", r.Ids());
    }

    [Fact]
    public void Review_and_weak_outside_sources_are_selected_but_seen_is_not()
    {
        var p = Sel.P([Sel.Weak("k1"), Sel.Mastered("m1", Sel.Today), Sel.Normal("s1", Sel.Now.AddDays(-3))]);

        var r = Sel.Run(Sel.Set([Sel.W("k1"), Sel.W("m1"), Sel.W("s1"), Sel.W("u1")]), p, Sel.Settings(sources: ["NAWL"]));

        Assert.Equal(["k1", "m1"], r.Ids().Order());
    }

    [Fact]
    public void Review_record_with_mismatched_id_is_not_duplicated()
    {
        // 2つの見出しが同じ WordId を持つ(手で書き換えた記録。読込では弾かれるが、直接渡されても一覧を重複させない)
        var p = LearningProgress.Empty with
        {
            Words = ImmutableDictionary<string, WordProgress>.Empty.WithComparers(StringComparer.Ordinal)
                .Add("m1", Sel.Mastered("m1", Sel.Today)).Add("m2", Sel.Mastered("m1", Sel.Today)),
        };

        // 20問なら再確認の上限が2になり、同じ単語が2回入りうる(10問だと上限1で重複が表に出ない)
        var s = Sel.Settings(count: 20);
        var r = Sel.Run(Sel.Set([Sel.W("m1"), Sel.W("m2")]), p, s);

        Assert.Equal(r.Plan.Length, r.Ids().Distinct().Count());
        Assert.NotNull(SessionManager.Start(Guid.NewGuid(), Sel.Now, r.Plan, s));
    }

    [Fact]
    public void Weak_record_with_mismatched_id_and_null_sources_do_not_throw()
    {
        var p = LearningProgress.Empty with
        {
            Words = ImmutableDictionary<string, WordProgress>.Empty.WithComparers(StringComparer.Ordinal).Add("k1", Sel.Weak("k2")),
        };
        var words = Sel.Set([Sel.W("k1"), Sel.W("u1") with { Sources = null! }, Sel.W("u2")]);

        var r = Sel.Run(words, p);

        Assert.DoesNotContain("u1", r.Ids());
        Assert.Contains("u2", r.Ids());
    }

    [Fact]
    public void Sources_filter_only_new_words()
    {
        var ngslWeak = Sel.W("k1");
        var words = Sel.Set([Sel.W("u1"), Sel.W("u2", sources: "nawl"), Sel.W("u3", sources: ["NGSL", "NAWL"]), ngslWeak]);
        var p = Sel.P([Sel.Weak("k1")]);

        var r = Sel.Run(words, p, Sel.Settings(sources: ["NAWL"]));

        Assert.Equal(["k1", "u2", "u3"], r.Ids().Order());
    }

    [Fact]
    public void Duplicate_ids_blank_words_and_unknown_progress_are_ignored()
    {
        var words = Sel.Set([Sel.W("u1"), Sel.W("u1", tier: 1), Sel.W("u2") with { Word = " " }, Sel.W("u3") with { Id = "" }]);
        var p = Sel.P([Sel.Weak("ghost")]);

        var r = Sel.Run(words, p);

        Assert.Equal(["u1"], r.Ids());
        Assert.NotNull(SessionManager.Start(Guid.NewGuid(), Sel.Now, r.Plan, Sel.Settings()));
    }

    [Fact]
    public void New_words_ordered_by_tier_then_frequency_with_missing_frequency_last_within_tier()
    {
        // 5問 = 新規3・苦手2。苦手を2語置き、新規の枠をちょうど3にする
        var words = Sel.Set([
            Sel.W("t3a", tier: 3, freq: 1), Sel.W("t1n", tier: 1), Sel.W("t2a", tier: 2, freq: 5), Sel.W("t1b", tier: 1, freq: 50),
            Sel.W("t1a", tier: 1, freq: 10), Sel.W("x1"), Sel.W("x2")]);
        var p = Sel.P([Sel.Weak("x1"), Sel.Weak("x2")]);

        var r = Sel.Run(words, p, Sel.Settings(count: 5));
        var tierOnlyTwo = Sel.Run(Sel.Set([Sel.W("t1n", tier: 1), Sel.W("t2a", tier: 2, freq: 5), Sel.W("t1a", tier: 1, freq: 10), Sel.W("x1"), Sel.W("x2")]), p, Sel.Settings(count: 4)); // 新規2

        Assert.Equal(["t1a", "t1b", "t1n"], r.Ids().Where(i => i.StartsWith('t')).Order());
        Assert.Equal(["t1a", "t1n"], tierOnlyTwo.Ids().Where(i => i.StartsWith('t')).Order()); // 頻度ありが先、頻度なしは Tier の中で最後(Tier 2 より先)
    }

    [Fact]
    public void Same_rank_new_words_are_ordered_by_seed()
    {
        var words = Sel.Set(Sel.Many("u", 6));
        var chosen = Enumerable.Range(1, 30).Select(seed => string.Join(",", Sel.Run(words, s: Sel.Settings(count: 5), seed: seed).Ids().Order())).Distinct().ToList();

        Assert.True(chosen.Count > 1); // 種によって外れる1語が変わる(30通りの種で決定的に確かめる)
    }

    [Fact]
    public void Same_seed_gives_same_plan_and_plan_has_no_duplicates()
    {
        var weak = Sel.Many("k", 10);
        var set = Sel.Set(Sel.Many("u", 20), weak);
        var p = Sel.P(weak.Select(w => Sel.Weak(w.Id)));

        var a = Sel.Run(set, p, seed: 7);
        var b = Sel.Run(set, p, seed: 7);

        Assert.Equal(a.Plan.Select(q => (q.WordId, q.Mode)), b.Plan.Select(q => (q.WordId, q.Mode)));
        Assert.Equal(a.Plan.Length, a.Ids().Distinct().Count());
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("sources")]
    [InlineData("manual")]
    [InlineData("noMastery")]
    [InlineData("weakOnly")]
    public void Empty_reasons(string kind)
    {
        var (words, settings, modes, expected) = kind switch
        {
            "empty" => (Sel.Set(), Sel.Settings(), Sel.AllModes, SelectionEmptyReason.NoWordsInSources),
            "sources" => (Sel.Set([Sel.W("u1")]), Sel.Settings(sources: ["BSL"]), Sel.AllModes, SelectionEmptyReason.NoWordsInSources),
            "manual" => (Sel.Set([Sel.W("u1")]), Sel.Settings(autoMix: false, manual: Audio), Sel.NoAudio, SelectionEmptyReason.ManualModeUnavailable),
            "noMastery" => (Sel.Set([Sel.W("u1", ja: false, audio: false)]), Sel.Settings(), Sel.AllModes, SelectionEmptyReason.NoEligibleWords),
            _ => (Sel.Set([Sel.W("u1")]), Sel.Settings(weakOnly: true), Sel.AllModes, SelectionEmptyReason.NoWeakWords),
        };

        var r = QuestionSelector.Select(words, LearningProgress.Empty, settings, modes, Sel.Now, new Random(1));

        Assert.Empty(r.Plan);
        Assert.Equal(expected, r.EmptyReason);
    }

    [Fact]
    public void Empty_reason_priority_and_scope()
    {
        // 語彙リストに単語なし と 手動モードが使えない が重なる → 語彙リストが先
        var both = QuestionSelector.Select(Sel.Set([Sel.W("u1")]), LearningProgress.Empty,
            Sel.Settings(autoMix: false, manual: Audio, sources: ["BSL"]), Sel.NoAudio, Sel.Now, new Random(1));
        Assert.Equal(SelectionEmptyReason.NoWordsInSources, both.EmptyReason);

        // リスト外の苦手だけがある(出せない)→ 語彙リストのせいではない
        var weakOutside = Sel.Run(Sel.Set([Sel.W("k1", ja: false, audio: false)]), Sel.P([Sel.Weak("k1")]), Sel.Settings(sources: ["BSL"]));
        Assert.Equal(SelectionEmptyReason.NoEligibleWords, weakOutside.EmptyReason);

        // リスト内の単語はどれも出せず、リスト外に出せる単語がある → 出せる単語なし
        var listIneligible = Sel.Run(Sel.Set([Sel.W("u1", ja: false, audio: false), Sel.W("u2", sources: "BSL")]), s: Sel.Settings(sources: ["NGSL"]));
        Assert.Equal(SelectionEmptyReason.NoEligibleWords, listIneligible.EmptyReason);
    }

    [Fact]
    public void Invalid_settings_or_unknown_ordering_throw()
    {
        Assert.Throws<ArgumentException>(() => Sel.Run(Sel.Set([Sel.W("u1")]), s: Sel.Settings(count: 0)));
        var unknown = Sel.Settings() with { Vocabulary = DefaultSettings.Create().Vocabulary with { Ordering = (NewWordOrdering)9 } };
        Assert.Throws<ArgumentException>(() => Sel.Run(Sel.Set([Sel.W("u1")]), s: unknown));
    }
}

public class QuestionSelectorModeTests(ITestOutputHelper output)
{
    private static Dictionary<LearningMode, int> CountModes(SelectionResult r) =>
        Enum.GetValues<LearningMode>().ToDictionary(m => m, m => r.Plan.Count(q => q.Mode == m));

    [Theory]
    [InlineData(10)]
    [InlineData(7)]
    public void All_eligible_words_follow_allocate_modes(int n)
    {
        var r = Sel.Run(Sel.Set(Sel.Many("u", n)), s: Sel.Settings(count: 10));

        var expected = QuestionPlanner.AllocateModes(n, new ModeWeights(), Sel.AllModes);
        Assert.Equal(expected.OrderBy(kv => kv.Key), CountModes(r).OrderBy(kv => kv.Key));
    }

    [Fact]
    public void No_audio_device_gives_4_0_3_3()
    {
        var r = Sel.Run(Sel.Set(Sel.Many("u", 10)), modes: Sel.NoAudio);

        Assert.Equal([4, 0, 3, 3], Enum.GetValues<LearningMode>().Select(m => CountModes(r)[m]));
    }

    [Fact]
    public void Skewed_eligibility_uses_only_eligible_modes_and_falls_back()
    {
        // 全単語が穴あき不可: 目標 3/3/2/2 のうち穴あき2は割り当てられず、重みのあるモードの優先順(日本語)に回る
        var words = Enumerable.Range(0, 10).Select(i => Sel.W($"u{i:D2}", blank: false)).ToArray();

        var r = Sel.Run(Sel.Set(words));

        Assert.All(r.Plan, q => Assert.Contains(q.Mode, q.EligibleModes));
        Assert.Equal([5, 3, 0, 2], Enum.GetValues<LearningMode>().Select(m => CountModes(r)[m]));
    }

    [Fact]
    public void Words_with_fewer_modes_get_their_modes_first()
    {
        // 6語は穴あき不可、4語は全モード可 → 合計は目標 3/3/2/2 どおり
        var words = Enumerable.Range(0, 6).Select(i => Sel.W($"a{i}", blank: false)).Concat(Enumerable.Range(0, 4).Select(i => Sel.W($"b{i}"))).ToArray();

        var r = Sel.Run(Sel.Set(words));

        Assert.All(r.Plan, q => Assert.Contains(q.Mode, q.EligibleModes));
        Assert.Equal([3, 3, 2, 2], Enum.GetValues<LearningMode>().Select(m => CountModes(r)[m]));
    }

    [Fact]
    public void Zero_weight_mode_is_used_only_as_last_fallback()
    {
        // 重み: 日本語0・音声3・穴あき0・フラッシュ3。単語は音声不可 → フラッシュの目標を使い切った後も、重み0の日本語・穴あきより重みのあるフラッシュ
        var s = Sel.Settings() with { Practice = Sel.Settings().Practice with { ModeWeights = new ModeWeights { Japanese = 0, Audio = 3, Blank = 0, Flash = 3 } } };

        var r = Sel.Run(Sel.Set(Enumerable.Range(0, 10).Select(i => Sel.W($"u{i:D2}", audio: false))), s: s);

        Assert.All(r.Plan, q => Assert.Equal(Flash, q.Mode));
    }

    [Fact]
    public void Weighted_modes_unavailable_fall_back_by_priority()
    {
        // 重みは音声だけ、端末に音声なし → 目標はすべて0。使える中で優先順の先頭(日本語)
        var s = Sel.Settings() with { Practice = Sel.Settings().Practice with { ModeWeights = new ModeWeights { Japanese = 0, Audio = 3, Blank = 0, Flash = 0 } } };

        var r = Sel.Run(Sel.Set(Sel.Many("u", 10)), s: s, modes: Sel.NoAudio);

        Assert.All(r.Plan, q => Assert.Equal(Japanese, q.Mode));
    }

    [Fact]
    public void Manual_mode_uses_selected_mode_and_excludes_ineligible_words()
    {
        var r = Sel.Run(Sel.Set([Sel.W("u1"), Sel.W("u2", blank: false), Sel.W("u3")]), s: Sel.Settings(autoMix: false, manual: Blank));

        Assert.Equal(["u1", "u3"], r.Ids().Order());
        Assert.All(r.Plan, q => Assert.Equal(Blank, q.Mode));
    }

    [Fact]
    public void Plan_starts_a_session_and_can_be_played_and_saved()
    {
        var weak = Sel.Many("k", 3);
        var r = Sel.Run(Sel.Set(Sel.Many("u", 2), weak), Sel.P(weak.Select(w => Sel.Weak(w.Id))));
        var session = SessionManager.Start(Guid.NewGuid(), Sel.Now, r.Plan, Sel.Settings());
        var progress = Sel.P(weak.Select(w => Sel.Weak(w.Id)));

        while (!session.IsCompleted)
        {
            var q = SessionManager.Current(session)!;
            var a = SessionManager.Answer(session, progress, q.Word, Sel.Now);
            session = a.Session;
            progress = a.Progress;
            if (a.SessionToSave is { } toSave)
            {
                Assert.Empty(SessionValidator.Validate(toSave, progress));
            }
        }

        Assert.Equal(5, session.Questions.Length);
    }

    [Fact]
    public void Selecting_from_5000_words_is_fast_enough_to_observe()
    {
        var words = Sel.Many("w", 5000).Select((w, i) => w with { Id = $"w{i:D4}", Word = $"w{i:D4}", PriorityTier = 1 + (i % 3), FrequencyRank = i }).ToArray();
        var weak = words.Take(500).Select(w => Sel.Weak(w.Id, 1 + (w.FrequencyRank!.Value % 6))).ToArray();
        var set = Sel.Set(words);
        var p = Sel.P(weak);

        var sw = Stopwatch.StartNew();
        var r = Sel.Run(set, p, Sel.Settings(count: 20));
        sw.Stop();

        output.WriteLine($"5,000語からの選択: {sw.Elapsed.TotalMilliseconds:F2} ms");
        Assert.Equal(20, r.Plan.Length);
    }
}
