using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Flow.Tests;

/// <summary>
/// 通常問題のスキップ(後回し)と保存失敗(P0-AC v0.2 AC-X01/08、.steering/20261115-skip-save-retry)。
/// 通常問題を A・B・C の順に固定したセッションを一時フォルダに置いて始める。
/// </summary>
public class FlowSkipTests
{
    private const string A = "alpha";
    private const string B = "bravo";
    private const string C = "charlie";

    private static readonly Guid SessionId = new("00000000-0000-0000-0000-00000000abcd");

    /// <summary>A・B・C の順の未完了セッション(まだ1問も答えていない)を保存済みの環境。</summary>
    private static FlowEnv EnvWithFixedOrder()
    {
        var env = new FlowEnv { WordsJson = Words.Json(new TestWord(A), new TestWord(B), new TestWord(C)) };
        var settings = DefaultSettings.Create();
        LearningMode[] eligible = [Japanese, Audio, Flash];
        var session = SessionManager.Start(
            SessionId,
            FlowEnv.T0,
            [new(A, A, Japanese, [.. eligible]), new(B, B, Japanese, [.. eligible]), new(C, C, Japanese, [.. eligible])],
            settings);
        env.Dir.Write(
            Persistence.ProgressRepository.FileName,
            ProgressJson.Serialize(new ProgressDocument { WordSetVersion = "v1", SourceVersion = "src1", Session = session }));
        return env;
    }

    private static PracticeFlow ResumeAt(FlowEnv env, string expectedWordId)
    {
        var flow = env.Start();
        flow.Resume();
        Assert.Equal(expectedWordId, flow.As<QuestionScreen>().Question.WordId);
        return flow;
    }

    [Fact]
    public void Skip_with_failed_saves_stays_on_A_then_moves_to_B_survives_restart_and_defers_A_once()
    {
        using var env = EnvWithFixedOrder();
        var flow = ResumeAt(env, A);
        var onA = flow.As<QuestionScreen>();
        Assert.True(onA.CanSkip);
        Assert.Equal(QuestionPhase.Normal, onA.Phase);
        var before = env.Dir.Snapshot();
        var skippedAt = env.Time.Now;

        // 1. A でスキップ。保存に失敗したら A に留まる
        env.Files.FailWrites = true;
        flow.Skip();
        var failed = flow.As<QuestionScreen>();
        Assert.Equal(A, failed.Question.WordId);
        Assert.NotNull(failed.SkipSaveFailure);
        Assert.NotNull(failed.Diagnostic);
        Assert.False(failed.CanSkip);

        // 2. 再保存も一度失敗。この間、回答・スキップ・中断・次へは受け付けない
        env.Time.Now = env.Time.Now.AddMinutes(1); // 再保存で時刻を作り直さないことを確かめるため
        flow.RetrySave();
        Assert.Equal(A, flow.As<QuestionScreen>().Question.WordId);
        Assert.NotNull(flow.As<QuestionScreen>().SkipSaveFailure);
        Assert.Throws<InvalidOperationException>(() => flow.Answer(A));
        Assert.Throws<InvalidOperationException>(flow.Skip);
        Assert.Throws<InvalidOperationException>(flow.Quit);
        Assert.Throws<InvalidOperationException>(flow.Next);
        Assert.Equal(before, env.Dir.Snapshot());
        Assert.Equal(A, flow.As<QuestionScreen>().Question.WordId);

        // 3. 次の再保存が成功すると B へ。A は後回しに1件だけ、採点は0件
        env.Files.FailWrites = false;
        env.Time.Now = env.Time.Now.AddMinutes(1);
        flow.RetrySave();
        var onB = flow.As<QuestionScreen>();
        Assert.Equal(B, onB.Question.WordId);
        Assert.Null(onB.SkipSaveFailure);
        Assert.Null(onB.Diagnostic);
        AssertSkippedOnceWithoutScoring(env, skippedAt);

        // 4. 再起動すると B から再開。後回しは1件のまま
        var restarted = env.Start();
        Assert.Equal(0, restarted.As<ResumePromptScreen>().Session.Answered);
        restarted.Resume();
        Assert.Equal(B, restarted.As<QuestionScreen>().Question.WordId);
        AssertSkippedOnceWithoutScoring(env, skippedAt);

        // 5. B・C に答え終えると A が後回し問題として出る。A はもうスキップできない
        restarted.AnswerCurrent(true);
        restarted.Next();
        Assert.Equal(C, restarted.As<QuestionScreen>().Question.WordId);
        restarted.AnswerCurrent(true);
        restarted.Next();
        var deferred = restarted.As<QuestionScreen>();
        Assert.Equal(SessionId, deferred.SessionId);
        Assert.Equal(A, deferred.Question.WordId);
        Assert.Equal(QuestionPhase.Deferred, deferred.Phase);
        Assert.False(deferred.CanSkip);
        Assert.Throws<InvalidOperationException>(restarted.Skip);
        Assert.Equal(A, restarted.As<QuestionScreen>().Question.WordId);
        Assert.Single(env.Progress().Session!.Skips);

        // 後回しの A への回答は元の通常枠(番号0・通常)の採点として1回だけ記録される
        restarted.AnswerCurrent(true);
        restarted.Next();
        var result = restarted.As<ResultScreen>();
        Assert.Equal(3, result.Summary.NormalCount);
        Assert.Equal(3, result.Summary.NormalAnswered);
        Assert.Equal(3, result.Summary.NormalCorrect);
        Assert.Equal(3, env.RecordedAnswers());
        Assert.Null(env.Progress().Session);
    }

    [Fact]
    public void Restart_while_skip_is_unsaved_resumes_at_A_with_no_skip_recorded()
    {
        using var env = EnvWithFixedOrder();
        var flow = ResumeAt(env, A);
        env.Files.FailWrites = true;
        flow.Skip();
        env.Files.FailWrites = false;

        var restarted = ResumeAt(env, A);

        Assert.True(restarted.As<QuestionScreen>().CanSkip);
        Assert.Empty(env.Progress().Session!.Skips);
    }

    [Fact]
    public void Phase_counters_follow_normal_then_deferred()
    {
        using var env = EnvWithFixedOrder();
        var flow = ResumeAt(env, A);
        Assert.Equal((1, 3), (flow.As<QuestionScreen>().Number, flow.As<QuestionScreen>().Total));

        flow.Skip();
        Assert.Equal((2, 3), (flow.As<QuestionScreen>().Number, flow.As<QuestionScreen>().Total));
        flow.Skip(); // B も後回し
        Assert.Equal((3, 3), (flow.As<QuestionScreen>().Number, flow.As<QuestionScreen>().Total));
        flow.AnswerCurrent(true);
        flow.Next();

        var deferred = flow.As<QuestionScreen>();
        Assert.Equal((A, QuestionPhase.Deferred, 1, 2), (deferred.Question.WordId, deferred.Phase, deferred.Number, deferred.Total));
        flow.AnswerCurrent(true);
        flow.Next();
        deferred = flow.As<QuestionScreen>();
        Assert.Equal((B, QuestionPhase.Deferred, 2, 2), (deferred.Question.WordId, deferred.Phase, deferred.Number, deferred.Total));
    }

    [Fact]
    public void Resume_is_refused_when_deferred_word_is_missing_from_word_data()
    {
        using var env = EnvWithFixedOrder();
        var flow = ResumeAt(env, A);
        flow.Skip();
        flow.AnswerCurrent(true);
        flow.Next();
        flow.AnswerCurrent(true);
        env.WordsJson = Words.Json(new TestWord(B), new TestWord(C)); // A が単語データから消えた

        var restarted = env.Start();

        Assert.False(restarted.As<ResumePromptScreen>().CanResume);
    }

    [Fact]
    public void Version1_mid_session_resumes_without_rescoring_and_first_save_protects_original()
    {
        using var env = EnvWithFixedOrder();
        var first = ResumeAt(env, A);
        first.AnswerCurrent(false); // A に誤答して保存(v2 で書かれる)
        var v1 = env.ProgressJsonText!.Replace(
            $"\"schemaVersion\": {ProgressDocument.CurrentSchemaVersion}", "\"schemaVersion\": 1", StringComparison.Ordinal)
            .Replace("\"skips\": [],", "", StringComparison.Ordinal)
            .Replace("\"skips\": []", "", StringComparison.Ordinal);
        env.Dir.Write(Persistence.ProgressRepository.FileName, v1); // v1 の通常途中の記録に置き換える(bak なしの想定は不要なので残す)
        var original = File.ReadAllBytes(env.Dir.File(Persistence.ProgressRepository.FileName));

        var flow = env.Start();
        Assert.Equal(original, File.ReadAllBytes(env.Dir.File(Persistence.ProgressRepository.FileName))); // 起動だけでは書かない
        var prompt = flow.As<ResumePromptScreen>();
        Assert.Equal(1, prompt.Session.Answered);
        flow.Resume();
        Assert.Equal(B, flow.As<QuestionScreen>().Question.WordId);
        Assert.Equal(1, env.RecordedAnswers());

        flow.Skip();

        Assert.Equal(C, flow.As<QuestionScreen>().Question.WordId);
        Assert.Equal(original, File.ReadAllBytes(env.Dir.File("progress.v1-protected.main.json")));
        var saved = env.Progress();
        Assert.Equal(ProgressDocument.CurrentSchemaVersion, saved.SchemaVersion);
        Assert.Equal(1, env.RecordedAnswers()); // 過去の回答を再採点しない・スキップは採点しない
        Assert.Single(saved.Progress.AppliedAnswerIds);
        Assert.Equal(1, Assert.Single(saved.Session!.Skips).QuestionIndex);
    }

    private static void AssertSkippedOnceWithoutScoring(FlowEnv env, DateTimeOffset skippedAt)
    {
        var saved = env.Progress();
        var session = saved.Session!;
        var skip = Assert.Single(session.Skips);
        Assert.Equal(0, skip.QuestionIndex);
        Assert.Equal(skippedAt, skip.SkippedAt);
        Assert.Empty(session.Answers);
        Assert.Empty(saved.Progress.AppliedAnswerIds);
        Assert.Equal(0, env.RecordedAnswers());
        Assert.Equal(ProgressDocument.CurrentSchemaVersion, saved.SchemaVersion);
    }
}
