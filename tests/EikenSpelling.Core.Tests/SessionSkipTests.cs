using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;

namespace EikenSpelling.Core.Tests;

/// <summary>通常問題のスキップ(後回し)。進行(Skips)と採点(Answers)を分ける(P0-AC v0.2 A節、SK-01〜03/05/14)。</summary>
public class SessionSkipTests
{
    private static readonly DateTimeOffset SkipAt = Jst.At(Jst.Day0, 9, 30);

    private static Runner Abc(int retry = 1) => new(S.Start(S.Settings(retry), S.Q("a"), S.Q("b"), S.Q("c")));

    private static void Skip(Runner r) => r.Skip(SkipAt);

    [Fact]
    public void Skip_moves_to_next_normal_without_scoring_or_touching_progress()
    {
        var r = Abc();
        var progress = r.Progress;

        Skip(r);

        Assert.Equal("b", SessionManager.Current(r.Session)!.WordId);
        Assert.Equal(QuestionPhase.Normal, SessionManager.Phase(r.Session));
        Assert.Empty(r.Session.Answers);
        Assert.Equal(new SessionSkip(0, SkipAt), Assert.Single(r.Session.Skips));
        Assert.Same(progress, r.Progress);
        Assert.Empty(SessionValidator.Validate(r.Session, r.Progress));
    }

    [Fact]
    public void Deferred_comes_after_all_normals_in_skip_order_and_cannot_be_skipped_again()
    {
        var r = Abc();
        Skip(r);            // a
        r.Answer(true);     // b
        Skip(r);            // c

        Assert.Equal("a", SessionManager.Current(r.Session)!.WordId);
        Assert.Equal(QuestionPhase.Deferred, SessionManager.Phase(r.Session));
        Assert.False(SessionManager.CanSkip(r.Session));
        Assert.Throws<InvalidOperationException>(() => SessionManager.Skip(r.Session, SkipAt));
        r.Answer(true);
        Assert.Equal("c", SessionManager.Current(r.Session)!.WordId);
        Assert.Equal(QuestionPhase.Deferred, SessionManager.Phase(r.Session));
        Assert.Empty(SessionValidator.Validate(r.Session, r.Progress));
    }

    [Fact]
    public void Deferred_answer_uses_the_original_normal_slot_id_and_counts_once()
    {
        var r = Abc();
        Skip(r);
        r.Answer(true);
        r.Answer(true);

        r.Answer(false); // 後回しの a

        Assert.Contains(new AnswerId(S.Id, 0, AttemptType.Normal), r.Progress.AppliedAnswerIds);
        Assert.Equal(1, r.Progress.Word("a")!.Modes.Values.Sum(m => m.NormalWrong));
        Assert.Equal(3, r.Progress.AppliedAnswerIds.Count);
    }

    [Fact]
    public void Retries_start_only_after_deferred_is_scored_in_normal_order()
    {
        var r = Abc();
        Skip(r);          // a
        r.Answer(false);  // b 誤答
        r.Answer(true);   // c
        Assert.Equal(3, r.Session.Questions.Length); // まだ a が後回しで残る

        r.Answer(false);  // a 誤答

        Assert.Equal(["a", "b"], r.Session.Questions.Skip(3).Select(q => q.WordId));
        Assert.All(r.Session.Questions.Skip(3), q => Assert.Equal(AttemptType.Retry, q.Attempt));
        Assert.Equal(QuestionPhase.Retry, SessionManager.Phase(r.Session));
        Assert.False(SessionManager.CanSkip(r.Session));
        Assert.Throws<InvalidOperationException>(() => SessionManager.Skip(r.Session, SkipAt));
        Assert.Empty(SessionValidator.Validate(r.Session, r.Progress));
    }

    [Fact]
    public void Summary_counts_deferred_answers_as_normal_and_ignores_retries()
    {
        var r = Abc();
        Skip(r);
        r.Answer(true);
        r.Answer(false);
        r.Answer(true);   // 後回しの a
        r.AnswerAll(true); // 再練習の c

        var summary = SessionSummary.From(r.Session);

        Assert.Equal(3, summary.NormalAnswered);
        Assert.Equal(2, summary.NormalCorrect);
        Assert.Equal(["c"], summary.MistakenWords.Select(w => w.WordId));
        Assert.Equal("c", Assert.Single(summary.Retries).WordId);
    }

    [Fact]
    public void Skip_after_completion_is_rejected()
    {
        var r = new Runner(S.Start(S.Settings(retry: 0), S.Q("a")));
        r.AnswerAll(true);

        Assert.False(SessionManager.CanSkip(r.Session));
        Assert.Null(SessionManager.Phase(r.Session));
        Assert.Throws<InvalidOperationException>(() => SessionManager.Skip(r.Session, SkipAt));
    }

    [Fact]
    public void Session_without_skips_keeps_positional_scoring()
    {
        var r = Abc();
        r.Answer(true);
        r.Answer(false);

        Assert.Equal([0, 1, 2], r.Session.AnswerOrder.ToArray());
        Assert.Equal(2, r.Session.CurrentIndex);
    }

    [Fact]
    public void Skipping_the_only_question_brings_it_back_as_deferred()
    {
        var r = new Runner(S.Start(S.Settings(), S.Q("a")));
        Skip(r);

        Assert.Equal("a", SessionManager.Current(r.Session)!.WordId);
        Assert.Equal(QuestionPhase.Deferred, SessionManager.Phase(r.Session));
        Assert.Empty(SessionValidator.Validate(r.Session, r.Progress));
    }

    [Fact]
    public void Skipping_all_normals_defers_them_all_in_skip_order()
    {
        var r = Abc();
        Skip(r);
        Skip(r);
        Skip(r);

        Assert.Equal([0, 1, 2], r.Session.AnswerOrder.ToArray());
        Assert.Equal(QuestionPhase.Deferred, SessionManager.Phase(r.Session));
        Assert.Empty(r.Session.Answers);
        Assert.Empty(SessionValidator.Validate(r.Session, r.Progress));
    }

    [Fact]
    public void Without_retries_session_completes_after_deferred_is_scored()
    {
        var r = Abc(retry: 0);
        Skip(r);
        r.Answer(false);
        r.Answer(false);

        var last = r.Answer(false); // 後回しの a

        Assert.True(last.Completed);
        Assert.Equal(3, r.Session.Questions.Length);
        Assert.Null(last.SessionToSave);
    }

    [Fact]
    public void Summary_lists_mistakes_in_normal_order_even_when_deferred()
    {
        var r = Abc();
        Skip(r);         // a
        r.Answer(false); // b
        r.Answer(true);  // c
        r.Answer(false); // 後回しの a

        Assert.Equal(["a", "b"], SessionSummary.From(r.Session).MistakenWords.Select(w => w.WordId));
    }

    // ---- 台帳の改ざん・不整合の検出(SK-14) ----

    private static PracticeSession SkippedA()
    {
        var r = Abc();
        Skip(r);
        return r.Session;
    }

    [Fact]
    public void Validator_rejects_duplicate_skip()
    {
        var s = SkippedA();
        var bad = s with { Skips = [new(0, SkipAt), new(0, SkipAt)] };

        Assert.NotEmpty(SessionValidator.Validate(bad, LearningProgress.Empty));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)] // 通常問題は 0〜2
    public void Validator_rejects_skip_outside_normal_range(int index)
    {
        var bad = SkippedA() with { Skips = [new(index, SkipAt)] };

        Assert.NotEmpty(SessionValidator.Validate(bad, LearningProgress.Empty));
    }

    [Fact]
    public void Validator_rejects_skips_out_of_order()
    {
        var bad = SkippedA() with { Skips = [new(1, SkipAt), new(0, SkipAt)] };

        Assert.NotEmpty(SessionValidator.Validate(bad, LearningProgress.Empty));
    }

    [Fact]
    public void Validator_rejects_skip_ahead_of_unanswered_earlier_normal()
    {
        // a に答えていないのに b をスキップした(先行スキップ)
        var bad = SkippedA() with { Skips = [new(1, SkipAt)] };

        Assert.NotEmpty(SessionValidator.Validate(bad, LearningProgress.Empty));
    }

    [Fact]
    public void Validator_rejects_skip_recorded_as_scored_answer_id()
    {
        var s = SkippedA();
        var progress = LearningProgress.Empty with { AppliedAnswerIds = [new AnswerId(S.Id, 0, AttemptType.Normal)] };

        Assert.NotEmpty(SessionValidator.Validate(s, progress));
    }

    [Fact]
    public void Validator_accepts_skip_on_later_normal_after_earlier_ones_scored()
    {
        var r = Abc();
        r.Answer(true);
        Skip(r); // b

        Assert.Equal([0, 2, 1], r.Session.AnswerOrder.ToArray());
        Assert.Empty(SessionValidator.Validate(r.Session, r.Progress));
    }

    // ---- JSON ----

    [Fact]
    public void Skips_round_trip_through_json()
    {
        var r = Abc();
        Skip(r);
        r.Answer(true);
        var json = ProgressJson.Serialize(new ProgressDocument { Progress = r.Progress, Session = r.Session });

        var restored = ProgressJson.Parse(json).Session!;

        Assert.Equal(r.Session.Skips.ToArray(), restored.Skips.ToArray());
        Assert.Equal("c", SessionManager.Current(restored)!.WordId);
        Assert.Equal(ProgressDocument.CurrentSchemaVersion, ProgressJson.ReadSchemaVersion(json));
    }

    [Fact]
    public void Version1_session_without_skips_reads_as_empty_ledger_and_same_next_question()
    {
        var r = Abc();
        r.Answer(false);
        var json = ProgressJson.Serialize(new ProgressDocument { SchemaVersion = 1, Progress = r.Progress, Session = r.Session })
            .Replace("\"skips\": [],", "", StringComparison.Ordinal)
            .Replace("\"skips\": []", "", StringComparison.Ordinal);
        Assert.DoesNotContain("skips", json, StringComparison.OrdinalIgnoreCase);

        var restored = ProgressJson.Parse(json);

        Assert.Equal(1, restored.SchemaVersion);
        Assert.True(restored.Session!.Skips.IsEmpty);
        Assert.Equal("b", SessionManager.Current(restored.Session)!.WordId);
        Assert.Empty(SessionValidator.Validate(restored.Session, restored.Progress));
    }

    [Fact]
    public void Null_skip_element_makes_session_unreadable()
    {
        var r = Abc();
        Skip(r);
        var json = ProgressJson.Serialize(new ProgressDocument { Progress = r.Progress, Session = r.Session });
        var broken = System.Text.RegularExpressions.Regex.Replace(
            json, "\"skips\": \\[[^\\]]*\\]", "\"skips\": [null]");

        var parsed = ProgressJson.ParseDetailed(broken);

        Assert.Null(parsed.Document.Session);
        Assert.True(parsed.SessionUnreadable);
    }
}
