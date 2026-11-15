using System.Collections.Immutable;
using System.Text.Json.Nodes;

using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Core.Tests;

/// <summary>セッションに保存する語彙セットの範囲と、その検査(AC-X02/12/14、ST-07/08)。</summary>
public class SessionScopeTests
{
    private static VocabularySetScope Scope(params string[] ids) => new(50, 2, 9, "v1", [.. ids]);

    private static PracticeSession Start(VocabularySetScope? scope, params string[] words) =>
        SessionManager.Start(S.Id, Jst.At(Jst.Day0, 9), [.. words.Select(w => S.Q(w))], S.Settings(), scope);

    private static ProgressDocument Doc(PracticeSession s, LearningProgress p) => new() { Progress = p, Session = s };

    [Fact]
    public void Start_keeps_the_scope_on_the_session()
    {
        var scope = Scope("a", "b", "c");

        var session = Start(scope, "a", "b");

        Assert.Equal(scope, session.Scope);
    }

    [Fact]
    public void Start_without_scope_leaves_scope_null_not_empty()
    {
        Assert.Null(Start(null, "a", "b").Scope);
    }

    [Fact]
    public void Start_rejects_a_plan_word_outside_the_scope()
    {
        Assert.Throws<ArgumentException>(() => Start(Scope("a", "b"), "a", "z"));
    }

    [Fact]
    public void Start_rejects_an_invalid_scope()
    {
        Assert.Throws<ArgumentException>(() => Start(Scope(), "a"));
        Assert.Throws<ArgumentException>(() => Start(Scope("a") with { UnitSize = 30 }, "a"));
    }

    [Fact]
    public void AC_X14_every_question_through_normal_deferred_and_retry_stays_in_the_scope()
    {
        var ids = new[] { "a", "b", "c", "d" };
        var session = Start(Scope([.. ids, "x", "y"]), "a", "b", "c", "d");
        var r = new Runner(session);

        r.Skip(Jst.At(Jst.Day0, 9, 30)); // a を後回し
        r.Answer(false);                 // b 誤答
        r.Answer(true);                  // c
        r.Answer(false);                 // d 誤答
        r.Answer(false);                 // 後回しの a も誤答 → 通常を採点し終えて再練習を足す
        while (!r.Session.IsCompleted)
        {
            r.Answer(false);
        }

        Assert.Contains(r.Session.Questions, q => q.Attempt == AttemptType.Retry);
        Assert.All(r.Session.Questions, q => Assert.Contains(q.WordId, r.Session.Scope!.WordIds));
    }

    [Fact]
    public void Round_trip_keeps_the_scope_and_a_scoped_session_validates()
    {
        var r = new Runner(Start(Scope("a", "b", "c"), "a", "b"));
        r.Answer(true);

        var parsed = ProgressJson.ParseDetailed(ProgressJson.Serialize(Doc(r.Session, r.Progress)));

        Assert.False(parsed.SessionUnreadable);
        var restored = parsed.Document.Session!;
        Assert.Equal(50, restored.Scope!.UnitSize);
        Assert.Equal(2, restored.Scope.Number);
        Assert.Equal(9, restored.Scope.TotalSets);
        Assert.Equal("v1", restored.Scope.WordSetVersion);
        Assert.Equal(["a", "b", "c"], restored.Scope.WordIds.ToArray());
        Assert.Empty(SessionValidator.Validate(restored, parsed.Document.Progress));
    }

    [Fact]
    public void An_unscoped_session_writes_no_scope_property()
    {
        var r = new Runner(Start(null, "a", "b"));

        var json = JsonNode.Parse(ProgressJson.Serialize(Doc(r.Session, r.Progress)))!;

        Assert.False(json["session"]!.AsObject().ContainsKey("scope"));
    }

    [Fact]
    public void Old_v2_session_json_without_scope_loads_as_unscoped_and_is_not_restricted()
    {
        var r = new Runner(Start(null, "a", "b"));
        r.Answer(true);
        var root = JsonNode.Parse(ProgressJson.Serialize(Doc(r.Session, r.Progress)))!.AsObject();
        root["session"]!.AsObject().Remove("scope");

        var parsed = ProgressJson.ParseDetailed(root.ToJsonString());

        Assert.Null(parsed.Document.Session!.Scope);
        Assert.Empty(SessionValidator.Validate(parsed.Document.Session, parsed.Document.Progress));
    }

    [Fact]
    public void An_explicit_empty_scope_in_json_is_rejected_not_treated_as_unscoped()
    {
        var r = new Runner(Start(Scope("a", "b"), "a", "b"));
        var root = JsonNode.Parse(ProgressJson.Serialize(Doc(r.Session, r.Progress)))!.AsObject();
        root["session"]!["scope"]!["wordIds"] = new JsonArray();

        var parsed = ProgressJson.ParseDetailed(root.ToJsonString());

        Assert.NotEmpty(SessionValidator.Validate(parsed.Document.Session!, parsed.Document.Progress));
    }

    [Fact]
    public void AC_X14_validator_rejects_a_session_with_a_question_outside_the_scope()
    {
        var r = new Runner(Start(Scope("a", "b", "c"), "a", "b"));
        var tampered = r.Session with { Scope = Scope("a", "c") }; // b が範囲外になる

        var problems = SessionValidator.Validate(tampered, r.Progress);

        Assert.Contains(problems, p => p.Contains("範囲"));
    }

    [Fact]
    public void Validator_rejects_a_retry_outside_the_scope_injected_into_the_queue()
    {
        var r = new Runner(Start(Scope("a", "b"), "a", "b"));
        var injected = r.Session with
        {
            Questions = r.Session.Questions.Add(new SessionQuestion("z", "z", Japanese, AttemptType.Retry, S.All)),
        };

        Assert.NotEmpty(SessionValidator.Validate(injected, r.Progress));
    }

    [Fact]
    public void Validator_rejects_a_malformed_scope()
    {
        var r = new Runner(Start(Scope("a", "b"), "a", "b"));

        Assert.NotEmpty(SessionValidator.Validate(r.Session with { Scope = Scope("a", "a", "b") }, r.Progress));
        Assert.NotEmpty(SessionValidator.Validate(r.Session with { Scope = Scope("a", "b") with { UnitSize = 10 } }, r.Progress));
        Assert.NotEmpty(SessionValidator.Validate(r.Session with { Scope = Scope("a", "b") with { WordIds = default } }, r.Progress));
    }

    [Fact]
    public void Resume_does_not_reselect_a_changed_set_the_saved_scope_and_plan_are_the_authority()
    {
        // 語彙更新後に同じセット番号が別の語になっても、保存した範囲と列は変わらない
        var r = new Runner(Start(Scope("a", "b", "c") with { WordSetVersion = "old" }, "a", "b"));

        var parsed = ProgressJson.ParseDetailed(ProgressJson.Serialize(Doc(r.Session, r.Progress)));

        Assert.Equal("old", parsed.Document.Session!.Scope!.WordSetVersion);
        Assert.Equal(["a", "b"], parsed.Document.Session.Questions.Select(q => q.WordId).ToArray());
    }

    [Fact]
    public void AC_X14_a_legit_retry_session_is_rejected_by_the_scope_check_alone_when_the_scope_is_narrowed()
    {
        // 再練習の列まで正しいセッション(他の検査は全部通る)。範囲だけを狭めると、範囲の検査だけが落とす
        var r = new Runner(Start(Scope("a", "b", "c"), "a", "b"));
        r.Answer(true);
        r.Answer(false); // b 誤答 → 再練習の b が列に足される
        Assert.Empty(SessionValidator.Validate(r.Session, r.Progress));

        var problems = SessionValidator.Validate(r.Session with { Scope = Scope("a", "c") }, r.Progress);

        Assert.Single(problems);
        Assert.Contains("範囲", problems[0]);
    }

    [Theory]
    [InlineData("""{ "wordIds": null }""")]
    [InlineData("""{ "wordIds": "a" }""")]
    [InlineData("""{ "wordIds": ["a", null] }""")]
    [InlineData("""{ "wordIds": [] }""")]
    [InlineData("""{ "wordIds": ["a", "a"] }""")]
    [InlineData("""{ "wordSetVersion": null }""")]
    [InlineData("""{ "unitSize": "50" }""")]
    [InlineData("""{ "number": 99 }""")]
    [InlineData("""{ "unitSize": 25 }""")]
    public void Malformed_scope_json_never_yields_a_usable_session_but_keeps_the_progress(string patch)
    {
        var r = new Runner(Start(Scope("a", "b", "c"), "a", "b"));
        r.Answer(true);
        var root = JsonNode.Parse(ProgressJson.Serialize(Doc(r.Session, r.Progress)))!.AsObject();
        var scope = root["session"]!["scope"]!.AsObject();
        foreach (var (key, value) in JsonNode.Parse(patch)!.AsObject().ToList())
        {
            scope[key] = value?.DeepClone();
        }

        var parsed = ProgressJson.ParseDetailed(root.ToJsonString());

        Assert.NotNull(parsed.Document.Progress.Word("a")); // 学習記録は読めている
        Assert.True(
            parsed.SessionUnreadable || SessionValidator.Validate(parsed.Document.Session!, parsed.Document.Progress).Count > 0,
            "壊れた範囲のセッションが検査を通った");
    }

    [Fact]
    public void An_explicit_null_scope_is_read_as_no_scope_like_other_optional_session_items()
    {
        var r = new Runner(Start(Scope("a", "b"), "a", "b"));
        var root = JsonNode.Parse(ProgressJson.Serialize(Doc(r.Session, r.Progress)))!.AsObject();
        root["session"]!["scope"] = null; // 手で消した(範囲の制限が外れるだけで、出題の列は固定のまま)

        var parsed = ProgressJson.ParseDetailed(root.ToJsonString());

        Assert.Null(parsed.Document.Session!.Scope);
        Assert.Equal(["a", "b"], parsed.Document.Session.Questions.Select(q => q.WordId).ToArray());
    }
}
