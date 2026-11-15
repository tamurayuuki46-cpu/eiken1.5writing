using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Core.Tests;

/// <summary>苦手一覧の行の詳細(UI v0.3 S-05・UC-05、AC-R04): 実誤入力・モード別の状態。「答えを見る」の不正解が最新でも実入力を失わない。</summary>
public class WeakWordRowDetailTests
{
    private static readonly DateTimeOffset Past = Jst.At(Jst.Day0, 8);
    private static readonly DateTimeOffset Later = Jst.At(Jst.Day0, 11);

    private static WeakWordRow RowFor(Learner learner, string id = "a")
    {
        var set = new WordSet { Words = [new WordEntry { Id = id, Word = id, Ja = "訳" }] };
        return Assert.Single(WeakWordList.Build(learner.Progress, set, DefaultSettings.Create(), Later.AddHours(1), WeakListSort.WeakFirst, weakOnly: false));
    }

    [Fact]
    public void Row_shows_latest_typed_input_with_its_mode_and_time()
    {
        var learner = new Learner();
        learner.Answer("a", Japanese, false, Past, input: "alpah");

        var row = RowFor(learner);

        Assert.Equal(("alpah", Japanese, Past), (row.LastWrongInput, row.LastWrongInputMode, row.LastWrongInputAt));
        Assert.Equal(Past, row.LastWrongAt);
    }

    [Fact]
    public void Row_keeps_typed_input_after_a_newer_revealed_mistake_and_shows_both_times()
    {
        var learner = new Learner();
        learner.Answer("a", Japanese, false, Past, input: "alpah");
        learner.Submit(new AnswerEvent(new AnswerId(Guid.NewGuid(), 0, AttemptType.Normal), "a", Japanese, false, null, Later, Revealed: true));

        var row = RowFor(learner);

        Assert.Equal("alpah", row.LastWrongInput);
        Assert.Equal(Past, row.LastWrongInputAt);
        Assert.Equal(Later, row.LastWrongAt); // 最後の誤答(答えを見るを含む)と実入力の時刻を混同しない
    }

    [Fact]
    public void Row_has_no_typed_input_when_only_revealed()
    {
        var learner = new Learner();
        learner.Submit(new AnswerEvent(new AnswerId(Guid.NewGuid(), 0, AttemptType.Normal), "a", Japanese, false, null, Later, Revealed: true));

        var row = RowFor(learner);

        Assert.Null(row.LastWrongInput);
        Assert.Null(row.LastWrongInputMode);
        Assert.Null(row.LastWrongInputAt);
        Assert.Equal(Later, row.LastWrongAt);
    }

    [Fact]
    public void Row_lists_every_mode_with_counts_and_accuracy_in_mode_order()
    {
        var learner = new Learner();
        learner.Answer("a", Japanese, false, Past);
        learner.Answer("a", Japanese, true, Past.AddMinutes(1));
        learner.Answer("a", Blank, false, Past.AddMinutes(2));
        learner.Answer("a", Blank, false, Past.AddMinutes(3), AttemptType.Retry);

        var modes = RowFor(learner).Modes;

        Assert.Equal([Japanese, Audio, Blank, Flash], modes.Select(m => m.Mode));
        var jp = modes[0];
        Assert.Equal((1, 1, 0, 0, 0.5), (jp.NormalCorrect, jp.NormalWrong, jp.RetryCorrect, jp.RetryWrong, jp.Accuracy));
        Assert.Equal((0, 0, 0, 0), (modes[1].NormalCorrect, modes[1].NormalWrong, modes[1].RetryCorrect, modes[1].RetryWrong));
        Assert.Null(modes[1].Accuracy); // 未出題は「未学習」
        var blank = modes[2];
        Assert.Equal((0, 1, 0, 1, 0.0), (blank.NormalCorrect, blank.NormalWrong, blank.RetryCorrect, blank.RetryWrong, blank.Accuracy));
    }
}
