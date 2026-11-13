using System.Collections.Immutable;
using System.Diagnostics;

using EikenSpelling.Core.Application;
using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

using static EikenSpelling.Core.Models.LearningMode;
using static EikenSpelling.Core.Models.LearningState;

namespace EikenSpelling.Core.Tests;

public class WeakWordListTests
{
    private static readonly TimeSpan Jst9 = TimeSpan.FromHours(9);

    // 日本時間 2026-11-02 12:00
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, Jst9);
    private static readonly DateOnly Today = new(2026, 11, 2);

    private static WordSet Set(params string[] ids) =>
        new() { Words = [.. ids.Select(id => new WordEntry { Id = id, Word = id + "-w", Ja = "訳" })] };

    private static LearningProgress Progress(params WordProgress[] words) =>
        LearningProgress.Empty with { Words = words.ToImmutableDictionary(w => w.WordId, StringComparer.Ordinal) };

    private static WordProgress W(string id, LearningState state = Weak) => new() { WordId = id, State = state };

    private static WordProgress With(WordProgress w, LearningMode mode, ModeRecord record) =>
        w with { Modes = w.Modes.SetItem(mode, record) };

    private static ImmutableArray<WeakWordRow> Build(
        LearningProgress progress, WordSet? set = null, LearningSettings? settings = null,
        WeakListSort sort = WeakListSort.WeakFirst, bool weakOnly = false, DateTimeOffset? now = null) =>
        WeakWordList.Build(progress, set ?? Set("a", "b", "c", "d", "e"), settings ?? DefaultSettings.Create(), now ?? Now, sort, weakOnly);

    private static LearningSettings Required(int days) =>
        DefaultSettings.Create() with { Mastery = DefaultSettings.Create().Mastery with { RequiredCorrectDays = days } };

    // ---- 対象 ----

    [Fact]
    public void Only_weak_and_mastered_words_are_listed()
    {
        var rows = Build(Progress(W("a", Normal), W("b", Weak), W("c", Mastered)));

        Assert.Equal(["b", "c"], rows.Select(r => r.WordId));
    }

    [Fact]
    public void Weak_only_hides_mastered_words()
    {
        var rows = Build(Progress(W("b", Weak), W("c", Mastered)), weakOnly: true);

        Assert.Equal(["b"], rows.Select(r => r.WordId));
    }

    [Fact]
    public void Row_uses_spelling_from_word_set()
    {
        var row = Assert.Single(Build(Progress(W("a"))));

        Assert.Equal("a-w", row.Word);
        Assert.True(row.InWordSet);
    }

    [Fact]
    public void Word_not_in_word_set_is_listed_by_id_with_mark()
    {
        var row = Assert.Single(Build(Progress(W("gone"))));

        Assert.Equal("gone", row.Word);
        Assert.False(row.InWordSet);
    }

    [Fact]
    public void Empty_progress_gives_empty_list()
    {
        Assert.Empty(Build(LearningProgress.Empty));
    }

    // ---- 誤答回数・正答率 ----

    [Fact]
    public void Mistakes_count_normal_questions_only()
    {
        var w = With(With(W("a"), Japanese, new ModeRecord { NormalWrong = 2, RetryWrong = 5 }), Audio, new ModeRecord { NormalWrong = 1 });

        Assert.Equal(3, Assert.Single(Build(Progress(w))).NormalMistakes);
    }

    [Fact]
    public void Accuracy_per_mode_is_null_when_not_asked_in_normal_questions()
    {
        var w = With(With(W("a"), Japanese, new ModeRecord { NormalCorrect = 1, NormalWrong = 3 }), Blank, new ModeRecord { RetryWrong = 2 });

        var accuracy = Assert.Single(Build(Progress(w))).Accuracy;

        Assert.Equal(new ModeAccuracy(0.25, null, null, null), accuracy);
    }

    // ---- 最後の誤答 ----

    [Fact]
    public void Last_mistake_is_the_latest_across_modes_including_retry_and_shown_in_japan_time()
    {
        var w = With(
            With(W("a"), Japanese, new ModeRecord { NormalWrong = 1, LastWrongInput = "old", LastWrongAt = Now.AddDays(-2) }),
            Audio, new ModeRecord { RetryWrong = 1, LastWrongInput = "new", LastWrongAt = new DateTimeOffset(2026, 11, 1, 15, 30, 0, TimeSpan.Zero) });

        var row = Assert.Single(Build(Progress(w)));

        Assert.Equal("new", row.LastWrongInput);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 0, 30, 0, Jst9), row.LastWrongAt);
        Assert.Equal(Jst9, row.LastWrongAt!.Value.Offset);
    }

    [Fact]
    public void Same_moment_in_several_modes_picks_mode_order()
    {
        var at = new DateTimeOffset(2026, 11, 1, 10, 0, 0, Jst9);
        var w = With(
            With(W("a"), Flash, new ModeRecord { NormalWrong = 1, LastWrongInput = "flash", LastWrongAt = at }),
            Audio, new ModeRecord { NormalWrong = 1, LastWrongInput = "audio", LastWrongAt = at.ToUniversalTime() }); // 同じ時点・オフセット違い

        Assert.Equal("audio", Assert.Single(Build(Progress(w))).LastWrongInput);
    }

    [Fact]
    public void Long_wrong_input_is_cut_to_the_storage_limit()
    {
        var w = With(W("a"), Japanese, new ModeRecord { NormalWrong = 1, LastWrongInput = new string('x', 300), LastWrongAt = Now });

        Assert.Equal(InputRules.TruncateForStorage(new string('x', 300)), Assert.Single(Build(Progress(w))).LastWrongInput);
    }

    [Fact]
    public void Extreme_last_wrong_moment_does_not_throw_and_shows_no_time()
    {
        var w = With(W("a"), Japanese, new ModeRecord { NormalWrong = 1, LastWrongInput = "x", LastWrongAt = DateTimeOffset.MaxValue });

        var row = Assert.Single(Build(Progress(w)));

        Assert.Null(row.LastWrongAt);
        Assert.Equal("x", row.LastWrongInput);
    }

    [Fact]
    public void No_mistake_records_give_null_last_mistake()
    {
        var row = Assert.Single(Build(Progress(W("a"))));

        Assert.Null(row.LastWrongInput);
        Assert.Null(row.LastWrongAt);
    }

    // ---- 克服状況 ----

    [Theory]
    [InlineData(3, 0, 3)]
    [InlineData(3, 1, 2)]
    [InlineData(3, 2, 1)]
    [InlineData(3, 3, 1)] // 必要回数を後から下げた記録: 次の対象正答で克服
    [InlineData(3, 4, 1)]
    [InlineData(1, 0, 1)]
    public void Remaining_for_mastery_comes_from_settings_and_is_at_least_one(int required, int streak, int remaining)
    {
        var w = W("a") with { MasteryStreak = streak };
        var settings = Required(required);

        var row = Assert.Single(Build(Progress(w), settings: settings));

        Assert.Equal(remaining, row.RemainingForMastery);
        Assert.Equal(streak, row.MasteryStreak);
        Assert.Equal(required, row.RequiredCorrectDays);
        Assert.Equal(WeakWordScorer.Score(w, settings, Now), row.WeakScore);
    }

    [Fact]
    public void Counted_today_switches_at_japan_midnight()
    {
        var w = W("a") with { MasteryStreak = 1, LastEligibleCorrectDate = Today };

        var before = Assert.Single(Build(Progress(w), now: new DateTimeOffset(2026, 11, 2, 14, 59, 59, TimeSpan.Zero)));
        var after = Assert.Single(Build(Progress(w), now: new DateTimeOffset(2026, 11, 2, 15, 0, 0, TimeSpan.Zero)));

        Assert.True(before.CountedToday);
        Assert.False(after.CountedToday);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(-1, true)]
    public void Mastered_row_shows_review_date_and_due(int daysFromToday, bool due)
    {
        var w = W("a", Mastered) with { NextReviewDate = Today.AddDays(daysFromToday) };

        var row = Assert.Single(Build(Progress(w)));

        Assert.Equal(Today.AddDays(daysFromToday), row.NextReviewDate);
        Assert.Equal(due, row.ReviewDue);
        Assert.Null(row.RemainingForMastery);
        Assert.Null(row.WeakScore);
        Assert.Null(row.CountedToday);
    }

    [Fact]
    public void Review_due_switches_at_japan_midnight()
    {
        var w = W("a", Mastered) with { NextReviewDate = new DateOnly(2026, 11, 3) };

        Assert.False(Assert.Single(Build(Progress(w), now: new DateTimeOffset(2026, 11, 2, 14, 59, 59, TimeSpan.Zero))).ReviewDue);
        Assert.True(Assert.Single(Build(Progress(w), now: new DateTimeOffset(2026, 11, 2, 15, 0, 0, TimeSpan.Zero))).ReviewDue);
    }

    [Fact]
    public void Weak_word_returned_from_mastered_hides_old_review_date()
    {
        var w = W("a", Weak) with { NextReviewDate = Today.AddDays(-5), MasteredAt = Now.AddDays(-30) };

        var row = Assert.Single(Build(Progress(w)));

        Assert.Null(row.NextReviewDate);
        Assert.Null(row.ReviewDue);
        Assert.NotNull(row.RemainingForMastery);
    }

    // ---- 並び ----

    [Fact]
    public void Weak_first_follows_scorer_order_then_mastered_by_review_date()
    {
        var weak = new[]
        {
            With(W("a"), Japanese, new ModeRecord { NormalWrong = 1, LastWrongAt = Now.AddDays(-30) }),
            With(W("b"), Japanese, new ModeRecord { NormalWrong = 5, LastWrongAt = Now }),
            W("c") with { LastNormalAskedAt = Now },
        };
        var mastered = new[]
        {
            W("d", Mastered) with { NextReviewDate = null },
            W("e", Mastered) with { NextReviewDate = Today.AddDays(9) },
            W("f", Mastered) with { NextReviewDate = Today.AddDays(1) },
            W("g", Mastered) with { NextReviewDate = null },
        };
        var settings = DefaultSettings.Create();

        var rows = Build(Progress([.. weak, .. mastered]), settings: settings);

        var expectedWeak = WeakWordScorer.Order(weak, settings, Now).Select(w => w.WordId);
        Assert.Equal([.. expectedWeak, "f", "e", "d", "g"], rows.Select(r => r.WordId));
    }

    [Fact]
    public void Recent_mistake_orders_newest_first_with_future_first_and_none_last()
    {
        var rows = Build(
            Progress(
                W("none"),
                With(W("old"), Japanese, new ModeRecord { NormalWrong = 1, LastWrongAt = Now.AddDays(-3) }),
                With(W("new"), Japanese, new ModeRecord { NormalWrong = 1, LastWrongAt = Now.AddHours(-1) }),
                With(W("future"), Japanese, new ModeRecord { NormalWrong = 1, LastWrongAt = Now.AddDays(5) }),
                With(W("tieB"), Japanese, new ModeRecord { NormalWrong = 1, LastWrongAt = Now.AddDays(-3) }),
                With(W("max"), Japanese, new ModeRecord { NormalWrong = 1, LastWrongAt = DateTimeOffset.MaxValue })),
            sort: WeakListSort.RecentMistake);

        Assert.Equal(["max", "future", "new", "old", "tieB", "none"], rows.Select(r => r.WordId));
    }

    // 同点の決め手が単語ID(Ordinal)であることを、同点の語を多数並べて確かめる
    // (辞書の列挙順はハッシュで決まり、入れる順を変えても変わらないため、入れる順の比較では決め手の抜けを検出できない)
    private static readonly string[] TiedIds =
        ["m", "B", "a", "c10", "c2", "Z", "b", "c1", "A", "k", "x", "Q", "d", "e9", "e10", "y"];

    private static string[] Ordinal(IEnumerable<string> ids) => [.. ids.Order(StringComparer.Ordinal)];

    [Fact]
    public void Recent_mistake_ties_are_ordered_by_word_id()
    {
        var at = Now.AddHours(-1);
        var rows = Build(
            Progress([.. TiedIds.Select(id => With(W(id), Japanese, new ModeRecord { NormalWrong = 1, LastWrongAt = at }))]),
            Set(TiedIds), sort: WeakListSort.RecentMistake);

        Assert.Equal(Ordinal(TiedIds), rows.Select(r => r.WordId).ToArray());
    }

    [Fact]
    public void Recent_mistake_words_without_mistake_are_last_by_word_id()
    {
        var rows = Build(Progress([.. TiedIds.Select(id => W(id))]), Set(TiedIds), sort: WeakListSort.RecentMistake);

        Assert.Equal(Ordinal(TiedIds), rows.Select(r => r.WordId).ToArray());
    }

    [Fact]
    public void Weak_first_ties_among_weak_words_are_ordered_by_word_id()
    {
        var rows = Build(Progress([.. TiedIds.Select(id => W(id))]), Set(TiedIds));

        Assert.Equal(Ordinal(TiedIds), rows.Select(r => r.WordId).ToArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Weak_first_ties_among_mastered_words_are_ordered_by_word_id(bool withDate)
    {
        DateOnly? date = withDate ? Today.AddDays(3) : null;
        var rows = Build(Progress([.. TiedIds.Select(id => W(id, Mastered) with { NextReviewDate = date })]), Set(TiedIds));

        Assert.Equal(Ordinal(TiedIds), rows.Select(r => r.WordId).ToArray());
    }

    [Fact]
    public void Extreme_dates_do_not_throw()
    {
        var rows = Build(Progress(
            W("a", Mastered) with { NextReviewDate = DateOnly.MaxValue },
            W("b") with { LastNormalAskedAt = DateTimeOffset.MaxValue, LastEligibleCorrectDate = DateOnly.MaxValue, MasteryStreak = 1 },
            W("c") with { LastNormalAskedAt = Now.AddDays(30) }));

        Assert.Equal(3, rows.Length);
        Assert.False(rows.Single(r => r.WordId == "a").ReviewDue);
        Assert.Equal("a", rows[^1].WordId); // 克服済みは苦手の後ろ
    }

    [Fact]
    public void Result_does_not_depend_on_dictionary_order()
    {
        var words = Enumerable.Range(0, 30)
            .Select(i => With(W($"w{i:D2}", i % 3 == 0 ? Mastered : Weak), Japanese, new ModeRecord { NormalWrong = i % 4, LastWrongAt = Now.AddHours(-(i % 5)) }))
            .ToArray();
        var forward = Progress(words);
        var backward = Progress([.. words.Reverse()]);
        var set = Set([.. words.Select(w => w.WordId)]);

        foreach (var sort in Enum.GetValues<WeakListSort>())
        {
            Assert.Equal(
                Build(forward, set, sort: sort).Select(r => r.WordId).ToArray(),
                Build(backward, set, sort: sort).Select(r => r.WordId).ToArray());
        }
    }

    [Fact]
    public void Undefined_sort_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(Progress(W("a")), sort: (WeakListSort)99));
    }

    [Fact]
    public void Row_text_does_not_reveal_wrong_input()
    {
        var w = With(W("a"), Japanese, new ModeRecord { NormalWrong = 1, LastWrongInput = "SECRET", LastWrongAt = Now });

        var row = Assert.Single(Build(Progress(w)));

        Assert.DoesNotContain("SECRET", row.ToString(), StringComparison.Ordinal);
        Assert.Equal("SECRET", row.LastWrongInput);
    }

    [Fact]
    public void Five_thousand_words_build_quickly()
    {
        var words = Enumerable.Range(0, 5000)
            .Select(i => With(W($"w{i:D4}", i % 2 == 0 ? Weak : Mastered) with { NextReviewDate = Today.AddDays(i % 30) },
                (LearningMode)(i % 4), new ModeRecord { NormalWrong = i % 7, NormalCorrect = i % 3, LastWrongInput = "x", LastWrongAt = Now.AddMinutes(-i) }))
            .ToArray();
        var progress = Progress(words);
        var set = Set([.. words.Select(w => w.WordId)]);
        Build(progress, set); // 準備運転

        var watch = Stopwatch.StartNew();
        var rows = Build(progress, set);
        var status = StudyStatus.From(set, progress, Today);
        watch.Stop();

        Assert.Equal(5000, rows.Length);
        Assert.Equal(5000, status.Weak + status.Mastered);
        // 目安は 100ms(v1.1 16.3「円滑な操作」)。CI の揺れで落ちないよう上限は緩くする
        Assert.True(watch.ElapsedMilliseconds < 1000, $"苦手一覧 5,000語: {watch.ElapsedMilliseconds}ms");
    }
}
