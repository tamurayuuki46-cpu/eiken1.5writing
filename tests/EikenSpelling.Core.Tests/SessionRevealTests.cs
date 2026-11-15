using EikenSpelling.Core.Application;
using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Core.Tests;

/// <summary>
/// 後回しの「答えを見る」(UI v0.3 S-04・UC-19、P0-AC v0.2 AC-X09/13、SK-04)。
/// 元の通常枠の不正解を1回だけ確定し、実入力は作らず、以前の実入力を保つ。
/// </summary>
public class SessionRevealTests
{
    private static readonly DateTimeOffset Past = Jst.At(Jst.Day0, 8);
    private static readonly DateTimeOffset SkipAt = Jst.At(Jst.Day0, 9, 30);
    private static readonly DateTimeOffset RevealAt = Jst.At(Jst.Day0, 11);

    /// <summary>a に過去の実入力あり。a をスキップ → b 正解 → c 誤答 → 後回しの a(Deferred)で止まる。</summary>
    private static Runner AtDeferredA(string pastInput = "alpah")
    {
        var learner = new Learner();
        learner.Answer("a", Japanese, false, Past, input: pastInput);
        var r = new Runner(S.Start(S.Settings(1), S.Q("a"), S.Q("b"), S.Q("c")), learner.Progress);
        r.Skip(SkipAt);
        r.Answer(true);
        r.Answer(false);
        Assert.Equal(("a", QuestionPhase.Deferred), (SessionManager.Current(r.Session)!.WordId, SessionManager.Phase(r.Session)!.Value));
        return r;
    }

    private static SessionAnswerResult Reveal(Runner r, DateTimeOffset? at = null)
    {
        var result = SessionManager.Reveal(r.Session, r.Progress, at ?? RevealAt);
        r.Apply(result);
        return result;
    }

    [Fact]
    public void Reveal_in_deferred_scores_original_normal_slot_as_one_wrong_and_creates_no_input()
    {
        var r = AtDeferredA();
        var wrongBefore = r.Progress.Word("a")!.Mode(Japanese).NormalWrong;

        var result = Reveal(r);

        Assert.False(result.IsCorrect);
        Assert.True(result.Revealed);
        Assert.Equal("a", result.CorrectWord);
        Assert.True(Assert.Single(r.Session.Answers.Skip(2)).Revealed);
        Assert.Contains(new AnswerId(S.Id, 0, AttemptType.Normal), r.Progress.AppliedAnswerIds);
        Assert.Equal(wrongBefore + 1, r.Progress.Word("a")!.Mode(Japanese).NormalWrong);
        Assert.Empty(SessionValidator.Validate(r.Session, r.Progress));
    }

    [Fact]
    public void Reveal_keeps_previous_typed_input_and_separates_its_time_from_the_new_mistake_time()
    {
        var r = AtDeferredA("alpah");

        Reveal(r);

        var mode = r.Progress.Word("a")!.Mode(Japanese);
        Assert.Equal("alpah", mode.LastWrongInput);
        Assert.Equal(RevealAt, mode.LastWrongAt);
        Assert.Equal(Past, mode.LastWrongInputAt);
        var typed = LatestMistake.TypedInputOf(r.Progress.Word("a")!);
        Assert.Equal(("alpah", Past, Japanese), (typed!.Input, typed.At, typed.Mode));
    }

    [Fact]
    public void Reveal_without_any_typed_input_leaves_no_input()
    {
        var r = new Runner(S.Start(S.Settings(1), S.Q("a"), S.Q("b")));
        r.Skip(SkipAt);
        r.Answer(true);

        Reveal(r);

        var mode = r.Progress.Word("a")!.Mode(Japanese);
        Assert.Null(mode.LastWrongInput);
        Assert.Null(mode.LastWrongInputAt);
        Assert.Equal(RevealAt, mode.LastWrongAt);
        Assert.Null(LatestMistake.TypedInputOf(r.Progress.Word("a")!));
        Assert.Equal(1, r.Progress.Word("a")!.NormalMistakes);
    }

    [Fact]
    public void Two_reveals_keep_the_original_input_time()
    {
        var r = AtDeferredA();
        Reveal(r);
        var again = r.Progress.Word("a")!;

        // 同じ単語の別セッションでもう一度答えを見る(入力時刻は最初のまま)
        var second = AnswerRecorder.Apply(
            r.Progress,
            new AnswerEvent(new AnswerId(Guid.NewGuid(), 0, AttemptType.Normal), "a", Japanese, false, null, RevealAt.AddHours(1), Revealed: true),
            S.Settings());

        Assert.Equal(Past, second.Progress.Word("a")!.Mode(Japanese).LastWrongInputAt);
        Assert.Equal("alpah", second.Progress.Word("a")!.Mode(Japanese).LastWrongInput);
        Assert.Equal(RevealAt.AddHours(1), second.Progress.Word("a")!.Mode(Japanese).LastWrongAt);
        Assert.Equal(again.Mode(Japanese).NormalWrong + 1, second.Progress.Word("a")!.Mode(Japanese).NormalWrong);
    }

    [Fact]
    public void A_later_typed_mistake_replaces_input_and_clears_the_separate_input_time()
    {
        var r = AtDeferredA();
        Reveal(r);

        var typed = AnswerRecorder.Apply(
            r.Progress,
            new AnswerEvent(new AnswerId(Guid.NewGuid(), 0, AttemptType.Normal), "a", Japanese, false, "newtypo", RevealAt.AddHours(2)),
            S.Settings());

        var mode = typed.Progress.Word("a")!.Mode(Japanese);
        Assert.Equal("newtypo", mode.LastWrongInput);
        Assert.Null(mode.LastWrongInputAt);
        Assert.Equal(RevealAt.AddHours(2), mode.LastWrongAt);
    }

    [Fact]
    public void Reveal_older_than_the_last_mistake_counts_the_wrong_but_keeps_times()
    {
        var learner = new Learner();
        learner.Answer("a", Japanese, false, RevealAt, input: "typo");

        var older = AnswerRecorder.Apply(
            learner.Progress,
            new AnswerEvent(new AnswerId(Guid.NewGuid(), 0, AttemptType.Normal), "a", Japanese, false, null, Past, Revealed: true),
            S.Settings());

        var mode = older.Progress.Word("a")!.Mode(Japanese);
        Assert.Equal(2, mode.NormalWrong);
        Assert.Equal(RevealAt, mode.LastWrongAt);
        Assert.Null(mode.LastWrongInputAt);
        Assert.Equal("typo", mode.LastWrongInput);
    }

    [Fact]
    public void Future_input_time_is_reported_as_a_warning()
    {
        var word = new WordProgress
        {
            WordId = "a",
            State = LearningState.Weak,
            Modes = System.Collections.Immutable.ImmutableDictionary<LearningMode, ModeRecord>.Empty.Add(
                Japanese, new ModeRecord { NormalWrong = 1, LastWrongInput = "x", LastWrongAt = Past, LastWrongInputAt = RevealAt.AddDays(3) }),
        };
        var progress = LearningProgress.Empty with { Words = LearningProgress.Empty.Words.Add("a", word) };

        var issues = ProgressValidator.Validate(progress, S.Settings(), RevealAt);

        Assert.Contains(issues, i => i.Code == "mode.future" && i.Severity == ProgressIssueSeverity.Warning);
    }

    [Fact]
    public void Reveal_applies_existing_weak_and_retry_rules()
    {
        var r = new Runner(S.Start(S.Settings(1), S.Q("a"), S.Q("b")));
        r.Skip(SkipAt);
        r.Answer(true);

        var result = Reveal(r);

        Assert.Equal(LearningState.Weak, r.Progress.Word("a")!.State); // 既定は1回の誤答で苦手
        Assert.Equal(LearningState.Weak, result.Change.StateAfter);
        Assert.Equal(["a"], r.Session.Questions.Skip(2).Select(q => q.WordId));
        Assert.Equal(AttemptType.Retry, r.Session.Questions[2].Attempt);
        Assert.Equal(QuestionPhase.Retry, SessionManager.Phase(r.Session));
    }

    [Fact]
    public void Reveal_then_correct_retries_keep_normal_score_at_one_of_three()
    {
        var r = AtDeferredA();
        Reveal(r);

        r.AnswerAll(true);

        Assert.True(r.Session.IsCompleted);
        var summary = SessionSummary.From(r.Session);
        Assert.Equal((3, 3, 1), (summary.NormalCount, summary.NormalAnswered, summary.NormalCorrect));
        Assert.Equal(["a", "c"], summary.MistakenWords.Select(w => w.WordId));
        Assert.True(summary.MistakenWords[0].Revealed);
        Assert.False(summary.MistakenWords[1].Revealed);
        Assert.All(summary.Retries, x => Assert.True(x.FinallyCorrect));
    }

    [Theory]
    [InlineData(QuestionPhase.Normal)]
    [InlineData(QuestionPhase.Retry)]
    public void Reveal_is_refused_outside_deferred_without_changing_anything(QuestionPhase phase)
    {
        var r = new Runner(S.Start(S.Settings(1), S.Q("a"), S.Q("b")));
        if (phase == QuestionPhase.Retry)
        {
            r.Answer(false);
            r.Answer(true);
        }

        Assert.Equal(phase, SessionManager.Phase(r.Session));
        Assert.False(SessionManager.CanReveal(r.Session));
        var session = r.Session;
        var progress = r.Progress;
        Assert.Throws<InvalidOperationException>(() => SessionManager.Reveal(session, progress, RevealAt));
        Assert.Same(session, r.Session);
        Assert.Same(progress, r.Progress);
    }

    [Fact]
    public void CanReveal_is_true_only_in_deferred_and_false_when_completed()
    {
        var r = AtDeferredA();
        Assert.True(SessionManager.CanReveal(r.Session));
        Assert.False(SessionManager.CanSkip(r.Session));

        Reveal(r);
        r.AnswerAll(true);

        Assert.False(SessionManager.CanReveal(r.Session));
        Assert.Throws<InvalidOperationException>(() => SessionManager.Reveal(r.Session, r.Progress, RevealAt));
    }

    [Fact]
    public void Revealed_event_must_be_wrong_and_is_idempotent_by_answer_id()
    {
        var progress = LearningProgress.Empty;
        var id = new AnswerId(S.Id, 0, AttemptType.Normal);

        Assert.Throws<ArgumentException>(() =>
            AnswerRecorder.Apply(progress, new AnswerEvent(id, "a", Japanese, true, null, RevealAt, Revealed: true), S.Settings()));

        var first = AnswerRecorder.Apply(progress, new AnswerEvent(id, "a", Japanese, false, null, RevealAt, Revealed: true), S.Settings());
        var duplicate = AnswerRecorder.Apply(first.Progress, new AnswerEvent(id, "a", Japanese, false, null, RevealAt, Revealed: true), S.Settings());

        Assert.False(first.IsDuplicate);
        Assert.True(duplicate.IsDuplicate);
        Assert.Same(first.Progress, duplicate.Progress);
        Assert.Equal(1, first.Progress.Word("a")!.Mode(Japanese).NormalWrong);
    }

    [Fact]
    public void Validator_rejects_a_revealed_answer_on_a_slot_that_was_not_deferred()
    {
        var r = AtDeferredA();
        Reveal(r);

        // 通常の段階で採点された枠(c: 2番目の採点)を答えを見るに書き換えた文書
        var forged = r.Session with { Answers = r.Session.Answers.SetItem(1, r.Session.Answers[1] with { Revealed = true }) };
        Assert.Contains(SessionValidator.Validate(forged, r.Progress), p => p.Contains("答えを見る"));

        // 正解になっている答えを見る
        var correct = r.Session with { Answers = r.Session.Answers.SetItem(2, r.Session.Answers[2] with { IsCorrect = true }) };
        Assert.Contains(SessionValidator.Validate(correct, r.Progress), p => p.Contains("答えを見る"));
    }

    [Fact]
    public void Validator_rejects_a_revealed_answer_in_the_retry_stage()
    {
        var r = AtDeferredA();
        Reveal(r);
        var inRetry = r.Session.Answers.Length; // 次の採点は再練習
        var forged = r.Session with
        {
            Answers = r.Session.Answers.Add(new SessionAnswer(false, LearningState.Weak, LearningState.Weak) { Revealed = true }),
        };

        Assert.Equal(inRetry + 1, forged.Answers.Length);
        Assert.Contains(SessionValidator.Validate(forged, r.Progress), p => p.Contains("答えを見る"));
    }

    [Fact]
    public void Revealed_and_input_time_round_trip_through_json_and_are_omitted_when_unused()
    {
        var r = AtDeferredA();
        Reveal(r);
        var json = ProgressJson.Serialize(new ProgressDocument { WordSetVersion = "v", SourceVersion = "s", Progress = r.Progress, Session = r.Session });

        var parsed = ProgressJson.Parse(json);

        Assert.True(parsed.Session!.Answers[2].Revealed);
        Assert.False(parsed.Session.Answers[0].Revealed);
        Assert.Equal(Past, parsed.Progress.Word("a")!.Mode(Japanese).LastWrongInputAt);

        var plain = new Runner(S.Start(S.Settings(1), S.Q("a")));
        plain.Answer(false);
        var plainJson = ProgressJson.Serialize(new ProgressDocument { Progress = plain.Progress, Session = plain.Session });
        Assert.DoesNotContain("revealed", plainJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lastWrongInputAt", plainJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Legacy_json_without_the_new_fields_still_reads_and_treats_input_time_as_last_wrong_time()
    {
        var learner = new Learner();
        learner.Answer("a", Japanese, false, Past, input: "alpah");
        var json = ProgressJson.Serialize(new ProgressDocument { Progress = learner.Progress });

        var parsed = ProgressJson.Parse(json);

        var typed = LatestMistake.TypedInputOf(parsed.Progress.Word("a")!);
        Assert.Equal(("alpah", Past), (typed!.Input, typed.At));
    }

    [Fact]
    public void Typed_input_is_the_latest_across_modes_even_when_a_newer_mistake_was_revealed_in_another_mode()
    {
        var learner = new Learner();
        learner.Answer("a", Japanese, false, Past, input: "jp-typo");
        learner.Answer("a", Blank, false, Past.AddHours(1), input: "blank-typo");
        learner.Submit(new AnswerEvent(new AnswerId(Guid.NewGuid(), 0, AttemptType.Normal), "a", Audio, false, null, Past.AddHours(5), Revealed: true));

        var word = learner.Word("a");

        Assert.Equal(Audio, LatestMistake.Of(word)!.Mode); // 苦手度の「直近の誤答」は答えを見るも含む
        var typed = LatestMistake.TypedInputOf(word)!;
        Assert.Equal(("blank-typo", Blank, Past.AddHours(1)), (typed.Input, typed.Mode, typed.At));
    }
}
