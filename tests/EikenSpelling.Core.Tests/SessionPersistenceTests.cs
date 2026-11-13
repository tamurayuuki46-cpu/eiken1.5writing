using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Core.Tests;

/// <summary>セッションの保存・読込と検査(design.md 4章「検査」「読込」)。</summary>
public class SessionJsonTests
{
    private static (PracticeSession Session, LearningProgress Progress) MidRetry()
    {
        var r = new Runner(S.Start(S.Settings(2), S.Q("a", Japanese, Japanese, Blank), S.Q("b")));
        r.Answer(false);
        r.Answer(true);
        r.Answer(false); // a の1回目の再練習で誤答 → 2回目が列の最後に
        return (r.Session, r.Progress);
    }

    private static ProgressDocument Doc(PracticeSession? s, LearningProgress p) => new() { Progress = p, Session = s };

    [Fact]
    public void Round_trip_keeps_session_and_resumes_at_same_question()
    {
        var (session, progress) = MidRetry();

        var parsed = ProgressJson.ParseDetailed(ProgressJson.Serialize(Doc(session, progress)));

        Assert.False(parsed.SessionUnreadable);
        var restored = parsed.Document.Session!;
        Assert.Equal(session.SessionId, restored.SessionId);
        Assert.Equal(session.Questions, restored.Questions, new QuestionComparer());
        Assert.Equal(session.Answers.ToArray(), restored.Answers.ToArray());
        Assert.Equal(2, restored.Settings.Practice.RetryCount);
        var next = SessionManager.Current(restored)!;
        Assert.Equal(("a", Blank, AttemptType.Retry), (next.WordId, next.Mode, next.Attempt));
        Assert.Empty(SessionValidator.Validate(restored, parsed.Document.Progress));
    }

    [Fact]
    public void Resumed_session_continues_with_same_retry_mode()
    {
        var (session, progress) = MidRetry();
        var restored = ProgressJson.Parse(ProgressJson.Serialize(Doc(session, progress)));
        var r = new Runner(restored.Session!, restored.Progress);

        r.AnswerAll(false);

        Assert.Equal([Blank, Blank], r.Session.Questions.Where(q => q.WordId == "a" && q.Attempt == AttemptType.Retry).Select(q => q.Mode));
        Assert.True(r.Last!.Completed);
    }

    [Fact]
    public void Save_before_last_normal_then_resume_creates_retries()
    {
        var r = new Runner(S.Start(null, S.Q("a"), S.Q("b")));
        r.Answer(false);
        var restored = ProgressJson.Parse(ProgressJson.Serialize(Doc(r.Session, r.Progress)));
        var resumed = new Runner(restored.Session!, restored.Progress);

        resumed.Answer(true);

        Assert.Equal(("a", AttemptType.Retry), (resumed.Session.Questions[2].WordId, resumed.Session.Questions[2].Attempt));
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 1, "progress": {} }""")]
    [InlineData("""{ "schemaVersion": 1, "progress": {}, "session": null }""")]
    public void Document_without_session_is_read(string json)
    {
        var parsed = ProgressJson.ParseDetailed(json);

        Assert.Null(parsed.Document.Session);
        Assert.False(parsed.SessionUnreadable);
    }

    [Theory]
    [InlineData("""{ "sessionId": "22222222-2222-2222-2222-222222222222" }""")]                // 必須項目の欠け
    [InlineData("""{ "sessionId": "x", "startedAt": "2026-11-01T09:00:00+09:00", "settings": {} }""")] // 型違い
    [InlineData("""{ "sessionId": "22222222-2222-2222-2222-222222222222", "startedAt": "2026-11-01T09:00:00+09:00", "settings": null }""")]
    [InlineData("""{ "sessionId": "22222222-2222-2222-2222-222222222222", "startedAt": "2026-11-01T09:00:00+09:00", "settings": {}, "questions": [null] }""")]
    [InlineData("""{ "sessionId": "22222222-2222-2222-2222-222222222222", "startedAt": "2026-11-01T09:00:00+09:00", "settings": {}, "answers": [null] }""")]
    [InlineData("""{ "sessionId": "22222222-2222-2222-2222-222222222222", "startedAt": "2026-11-01T09:00:00+09:00", "settings": { "mastery": { "reviewLimits": [null] } } }""")]
    public void Unreadable_session_is_dropped_but_progress_is_read(string sessionJson)
    {
        var json = """{ "schemaVersion": 1, "wordSetVersion": "ws", "progress": {}, "session": """ + sessionJson + " }";

        var parsed = ProgressJson.ParseDetailed(json);

        Assert.True(parsed.SessionUnreadable);
        Assert.Null(parsed.Document.Session);
        Assert.Equal("ws", parsed.Document.WordSetVersion);
    }

    private sealed class QuestionComparer : IEqualityComparer<SessionQuestion>
    {
        public bool Equals(SessionQuestion? x, SessionQuestion? y) =>
            x!.WordId == y!.WordId && x.Word == y.Word && x.Mode == y.Mode && x.Attempt == y.Attempt
            && x.EligibleModes.SequenceEqual(y.EligibleModes);

        public int GetHashCode(SessionQuestion obj) => obj.WordId.GetHashCode(StringComparison.Ordinal);
    }
}

public class SessionValidatorTests
{
    private static (PracticeSession Session, LearningProgress Progress) Valid()
    {
        var r = new Runner(S.Start(S.Settings(2), S.Q("a"), S.Q("b")));
        r.Answer(false);
        r.Answer(true);
        return (r.Session, r.Progress); // 列: a, b, a(再練習)。位置 2
    }

    private static bool Invalid(PracticeSession s, LearningProgress p) => SessionValidator.Validate(s, p).Count > 0;

    [Fact]
    public void Valid_session_has_no_problems()
    {
        var (s, p) = Valid();

        Assert.Empty(SessionValidator.Validate(s, p));
    }

    [Fact]
    public void Empty_session_id_or_invalid_snapshot_is_invalid()
    {
        var (s, p) = Valid();

        Assert.True(Invalid(s with { SessionId = Guid.Empty }, p));
        Assert.True(Invalid(s with { Settings = S.Settings(retry: 9) }, p));
        Assert.True(Invalid(s with { Settings = s.Settings with { SettingsVersion = 99 } }, p));
    }

    [Fact]
    public void Null_elements_are_invalid()
    {
        var (s, p) = Valid();

        Assert.True(Invalid(s with { Questions = s.Questions.SetItem(0, null!) }, p));
        Assert.True(Invalid(s with { Answers = s.Answers.SetItem(0, null!) }, p));
    }

    [Fact]
    public void Retry_for_correct_word_is_invalid()
    {
        var (s, p) = Valid();
        var bRetry = s.Questions[2] with { WordId = "b", Word = "b" };

        Assert.True(Invalid(s with { Questions = s.Questions.SetItem(2, bRetry) }, p));
    }

    [Fact]
    public void Too_many_retries_or_retry_before_normal_end_is_invalid()
    {
        var (s, p) = Valid();
        var extra = s.Questions.Add(s.Questions[2]).Add(s.Questions[2]); // a の再練習が3件(RetryCount 2 を超える)
        var early = s with { Questions = [s.Questions[0], s.Questions[2], s.Questions[1]], Answers = [s.Answers[0]] };

        Assert.True(Invalid(s with { Questions = extra }, p));
        Assert.True(Invalid(early, p));
    }

    [Fact]
    public void Wrong_retry_mode_is_invalid()
    {
        var (s, p) = Valid();
        var otherMode = s.Questions[2] with { Mode = Flash };

        Assert.True(Invalid(s with { Questions = s.Questions.SetItem(2, otherMode) }, p));
    }

    [Fact]
    public void Missing_retry_after_last_wrong_normal_is_invalid()
    {
        var (s, p) = Valid();

        Assert.True(Invalid(s with { Questions = s.Questions.RemoveAt(2) }, p));
    }

    [Fact]
    public void Completed_session_is_invalid()
    {
        var (s, p) = Valid();

        Assert.True(Invalid(s with { Answers = s.Answers.Add(new SessionAnswer(true, LearningState.Weak, LearningState.Weak)) }, p));
    }

    [Fact]
    public void Duplicate_normal_word_or_ineligible_mode_is_invalid()
    {
        var (s, p) = Valid();
        var dup = s.Questions.SetItem(1, s.Questions[0]);
        var ineligible = s.Questions.SetItem(1, s.Questions[1] with { EligibleModes = [Flash] });

        Assert.True(Invalid(s with { Questions = dup }, p));
        Assert.True(Invalid(s with { Questions = ineligible }, p));
    }

    [Fact]
    public void Retry_word_missing_from_progress_is_invalid()
    {
        var (s, p) = Valid();

        Assert.True(Invalid(s, p with { Words = p.Words.Remove("a") }));
    }

    [Fact]
    public void Too_many_normal_questions_is_invalid()
    {
        var big = S.Start(null, S.Words(100));
        var tooBig = big with { Questions = big.Questions.Add(big.Questions[0] with { WordId = "w100", Word = "w100" }) };

        Assert.True(Invalid(tooBig, LearningProgress.Empty));
    }
}
