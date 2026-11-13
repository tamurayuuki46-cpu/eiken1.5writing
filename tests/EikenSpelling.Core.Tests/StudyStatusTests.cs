using System.Collections.Immutable;

using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;

using static EikenSpelling.Core.Models.LearningState;

namespace EikenSpelling.Core.Tests;

public class StudyStatusTests
{
    private static readonly DateOnly Today = new(2026, 11, 2);

    private static WordSet Set(params string[] ids) =>
        new() { Words = [.. ids.Select(id => new WordEntry { Id = id, Word = id, Ja = "訳" })] };

    private static LearningProgress Progress(params WordProgress[] words) =>
        LearningProgress.Empty with { Words = words.ToImmutableDictionary(w => w.WordId, StringComparer.Ordinal) };

    private static WordProgress W(string id, LearningState state, DateOnly? next = null) =>
        new() { WordId = id, State = state, NextReviewDate = next };

    [Fact]
    public void No_records_means_every_word_is_unseen()
    {
        var status = StudyStatus.From(Set("a", "b", "c"), LearningProgress.Empty, Today);

        Assert.Equal(new StudyStatus(3, 0, 0, 0, 0), status);
    }

    [Fact]
    public void Words_are_counted_by_state()
    {
        var status = StudyStatus.From(
            Set("a", "b", "c", "d", "e"),
            Progress(W("a", Normal), W("b", Weak), W("c", Weak), W("d", Mastered, Today.AddDays(3))),
            Today);

        Assert.Equal(new StudyStatus(Unseen: 1, Normal: 1, Weak: 2, Mastered: 1, ReviewDue: 0), status);
    }

    [Theory]
    [InlineData(1, 0)] // 予定日は明日
    [InlineData(0, 1)] // 今日
    [InlineData(-1, 1)] // 昨日(期限切れ)
    public void Review_due_counts_mastered_words_whose_date_has_come(int daysFromToday, int due)
    {
        var status = StudyStatus.From(Set("a"), Progress(W("a", Mastered, Today.AddDays(daysFromToday))), Today);

        Assert.Equal(due, status.ReviewDue);
        Assert.Equal(1, status.Mastered);
    }

    [Fact]
    public void Mastered_without_review_date_is_not_due()
    {
        Assert.Equal(0, StudyStatus.From(Set("a"), Progress(W("a", Mastered)), Today).ReviewDue);
    }

    [Fact]
    public void Weak_word_with_old_review_date_is_not_due()
    {
        // 克服 → 苦手に戻った単語は古い予定日が残っている
        var status = StudyStatus.From(Set("a"), Progress(W("a", Weak, Today.AddDays(-10))), Today);

        Assert.Equal(0, status.ReviewDue);
        Assert.Equal(1, status.Weak);
    }

    [Fact]
    public void Records_of_words_not_in_word_set_are_not_counted()
    {
        var status = StudyStatus.From(Set("a"), Progress(W("gone", Weak), W("a", Normal)), Today);

        Assert.Equal(new StudyStatus(0, 1, 0, 0, 0), status);
    }

    [Fact]
    public void Empty_word_set_counts_nothing()
    {
        Assert.Equal(StudyStatus.Empty, StudyStatus.From(Set(), Progress(W("a", Weak)), Today));
        Assert.Equal(new StudyStatus(0, 0, 0, 0, 0), StudyStatus.Empty);
    }

    [Fact]
    public void Null_arguments_throw()
    {
        Assert.Throws<ArgumentNullException>(() => StudyStatus.From(null!, LearningProgress.Empty, Today));
        Assert.Throws<ArgumentNullException>(() => StudyStatus.From(Set(), null!, Today));
    }
}
