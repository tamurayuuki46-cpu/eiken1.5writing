using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;
using EikenSpelling.Persistence;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Flow.Tests;

/// <summary>
/// ロードマップ②(.steering/20261115-result-weak-review): 後回しの「答えを見る」と、結果 → 苦手一覧 → 戻る。
/// P0-AC v0.2 AC-X09/13、AC-R04/10、UC-05/06/16〜23。通常問題を A・B・C の順に固定し、A に過去の実入力を1件置く。
/// </summary>
public class FlowRevealTests
{
    private const string A = "alpha";
    private const string B = "bravo";
    private const string C = "charlie";
    private const string PastTypo = "alpah";

    private static readonly Guid SessionId = new("00000000-0000-0000-0000-00000000beef");
    private static readonly DateTimeOffset PastAt = FlowEnv.T0.AddDays(-1);

    /// <summary>A に過去の誤入力が1件ある学習記録と、A・B・C の順の未完了セッション(まだ1問も答えていない)を置いた環境。</summary>
    private static FlowEnv EnvWithPastTypoOnA()
    {
        var env = new FlowEnv { WordsJson = Words.Json(new TestWord(A), new TestWord(B), new TestWord(C)) };
        var settings = DefaultSettings.Create();
        var past = AnswerRecorder.Apply(
            LearningProgress.Empty,
            new AnswerEvent(new AnswerId(new Guid(9, 0, 0, new byte[8]), 0, AttemptType.Normal), A, Japanese, false, PastTypo, PastAt),
            settings).Progress;
        LearningMode[] eligible = [Japanese, Audio, Flash];
        var session = SessionManager.Start(
            SessionId,
            FlowEnv.T0,
            [new(A, A, Japanese, [.. eligible]), new(B, B, Japanese, [.. eligible]), new(C, C, Japanese, [.. eligible])],
            settings);
        env.Dir.Write(
            ProgressRepository.FileName,
            ProgressJson.Serialize(new ProgressDocument
            {
                WordSetVersion = "v1",
                SourceVersion = "src1",
                Progress = PruneIds(past),
                Session = session,
            }));
        return env;
    }

    private static LearningProgress PruneIds(LearningProgress progress) => SessionManager.PruneAnswerIds(progress, null);

    private static PracticeFlow ResumeAt(FlowEnv env, string expectedWordId)
    {
        var flow = env.Start();
        flow.Resume();
        Assert.Equal(expectedWordId, flow.As<QuestionScreen>().Question.WordId);
        return flow;
    }

    /// <summary>① A を Skip・B 正解・C 誤答して、後回しの A(Deferred)の問題にする。</summary>
    private static PracticeFlow AtDeferredA(FlowEnv env)
    {
        var flow = ResumeAt(env, A);
        flow.Skip();
        flow.AnswerCurrent(true);
        flow.Next();
        flow.AnswerCurrent(false);
        flow.Next();
        var deferred = flow.As<QuestionScreen>();
        Assert.Equal((A, QuestionPhase.Deferred), (deferred.Question.WordId, deferred.Phase));
        return flow;
    }

    private static ModeRecord JapaneseRecord(FlowEnv env, string id) => env.Progress().Progress.Word(id)!.Mode(Japanese);

    // ---- 最優先の受入シナリオ(依頼文 ①〜⑥) ----

    [Fact]
    public void Acceptance_reveal_with_failed_save_restart_keeps_one_wrong_and_past_input_and_result_stays_one_of_three()
    {
        using var env = EnvWithPastTypoOnA();
        var flow = AtDeferredA(env);
        var deferred = flow.As<QuestionScreen>();
        Assert.True(deferred.CanReveal);
        Assert.False(deferred.CanSkip);
        var wrongBefore = JapaneseRecord(env, A).NormalWrong;

        // ② 答えを見る → 保存に失敗 → 判定画面に留まる(次へ・やめるは使えない)
        env.Files.FailWrites = true;
        flow.Reveal();
        var failed = flow.As<FeedbackScreen>();
        Assert.True(failed.Revealed);
        Assert.False(failed.IsCorrect);
        Assert.Equal(A, failed.CorrectWord);
        Assert.Empty(failed.Diff); // 今回の入力との差分は作らない
        Assert.False(failed.Saved);
        Assert.NotNull(failed.SaveFailure);
        Assert.Throws<InvalidOperationException>(flow.Next);
        Assert.Throws<InvalidOperationException>(flow.Reveal);
        Assert.Throws<InvalidOperationException>(() => flow.Answer(A));

        // 再保存も一度失敗。成功すると保存済みになる(採点・記録は1回分のまま)
        env.Time.Now = env.Time.Now.AddMinutes(1);
        flow.RetrySave();
        Assert.False(flow.As<FeedbackScreen>().Saved);
        env.Files.FailWrites = false;
        flow.RetrySave();
        Assert.True(flow.As<FeedbackScreen>().Saved);
        Assert.True(flow.As<FeedbackScreen>().Revealed);

        // 再起動 → ③ A の通常不正解は1件だけ。過去の実入力は保持され、架空の入力はない
        var restarted = env.Start();
        Assert.Equal(3, restarted.As<ResumePromptScreen>().Session.Answered);
        var saved = env.Progress();
        Assert.Equal(wrongBefore + 1, JapaneseRecord(env, A).NormalWrong);
        Assert.Equal(PastTypo, JapaneseRecord(env, A).LastWrongInput);
        Assert.Equal(PastAt, JapaneseRecord(env, A).LastWrongInputAt);
        Assert.NotEqual(A, JapaneseRecord(env, A).LastWrongInput);
        Assert.True(saved.Session!.Answers[2].Revealed);
        Assert.Equal(3, saved.Progress.AppliedAnswerIds.Count(id => id.SessionId == SessionId));
        Assert.Contains(new AnswerId(SessionId, 0, AttemptType.Normal), saved.Progress.AppliedAnswerIds);

        // ④ 再練習(A・C)を全問正解しても、結果の通常成績は 1/3 のまま
        restarted.Resume();
        var retry = restarted.As<QuestionScreen>();
        Assert.Equal((QuestionPhase.Retry, A), (retry.Phase, retry.Question.WordId));
        Assert.False(retry.CanSkip);
        Assert.False(retry.CanReveal);
        Assert.Throws<InvalidOperationException>(restarted.Skip);
        Assert.Throws<InvalidOperationException>(restarted.Reveal);
        var result = restarted.AnswerAll(_ => true);
        Assert.Equal((3, 3, 1), (result.Summary.NormalCount, result.Summary.NormalAnswered, result.Summary.NormalCorrect));
        Assert.Equal([A, C], result.Summary.MistakenWords.Select(w => w.WordId));
        Assert.True(result.Summary.MistakenWords[0].Revealed);
        Assert.All(result.Summary.Retries, r => Assert.True(r.FinallyCorrect));
        Assert.Null(env.Progress().Session);
        Assert.Equal(PastTypo, JapaneseRecord(env, A).LastWrongInput); // 再練習の正解でも実入力は消えない

        // ⑤ 結果 → 苦手一覧 → 戻る で同じ 1/3 の結果に戻る。何も書かず、完了したセッションも復活しない
        var before = env.Dir.Snapshot();
        restarted.OpenWeakList();
        var list = restarted.As<WeakListScreen>();
        Assert.Equal(WeakListOrigin.Result, list.Origin);
        var rowA = list.Rows.Single(r => r.WordId == A);
        Assert.Equal(PastTypo, rowA.LastWrongInput); // 行内詳細: 直近に本人が入力した誤綴り
        Assert.Equal(Japanese, rowA.LastWrongInputMode);
        Assert.Equal(4, rowA.Modes.Length);
        restarted.ChangeWeakList(WeakListSort.RecentMistake, weakOnly: false);
        Assert.Equal(WeakListOrigin.Result, restarted.As<WeakListScreen>().Origin);
        restarted.CloseWeakList();
        var back = restarted.As<ResultScreen>();
        Assert.Equal((3, 3, 1), (back.Summary.NormalCount, back.Summary.NormalAnswered, back.Summary.NormalCorrect));
        Assert.Same(result, back);
        Assert.Equal(before, env.Dir.Snapshot());
        Assert.Null(env.Progress().Session);
        Assert.Throws<InvalidOperationException>(restarted.Resume);

        // ホームから開いた場合はホームに戻る
        restarted.GoHome();
        restarted.OpenWeakList();
        Assert.Equal(WeakListOrigin.Home, restarted.As<WeakListScreen>().Origin);
        restarted.CloseWeakList();
        Assert.IsType<HomeScreen>(restarted.Screen);
        Assert.Equal(before, env.Dir.Snapshot());

        // ⑥ 苦手一覧から一括復習を開始できる(苦手 A・C だけ)
        restarted.OpenWeakList();
        restarted.StartWeakPractice(restarted.As<WeakListScreen>().Defaults);
        var weakPractice = restarted.As<QuestionScreen>();
        Assert.Equal(QuestionPhase.Normal, weakPractice.Phase);
        Assert.Contains(weakPractice.Question.WordId, new[] { A, C });
    }

    [Fact]
    public void Weak_review_with_no_target_shows_reason_stays_on_list_and_can_return_to_the_result()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.StartPractice(Choices.Manual(Japanese, 3));
        var result = flow.AnswerAll(_ => true); // 全問正解 → 苦手 0 語
        var before = env.Dir.Snapshot();

        flow.OpenWeakList();
        Assert.Empty(flow.As<WeakListScreen>().Rows);
        flow.StartWeakPractice(flow.As<WeakListScreen>().Defaults);

        var list = flow.As<WeakListScreen>();
        Assert.Equal(SelectionEmptyReason.NoWeakWords, list.EmptyReason);
        Assert.Equal(WeakListOrigin.Result, list.Origin);
        Assert.Equal(before, env.Dir.Snapshot()); // 0件では何も書かない
        flow.CloseWeakList();
        Assert.Same(result, flow.As<ResultScreen>());
    }

    // ---- 答えを見る: 受け付ける段階・保存失敗・再起動 ----

    [Fact]
    public void Reveal_is_refused_in_normal_phase_and_changes_nothing()
    {
        using var env = EnvWithPastTypoOnA();
        var flow = ResumeAt(env, A);
        var before = env.Dir.Snapshot();

        Assert.False(flow.As<QuestionScreen>().CanReveal);
        Assert.Throws<InvalidOperationException>(flow.Reveal);

        Assert.Equal(A, flow.As<QuestionScreen>().Question.WordId);
        Assert.Equal(before, env.Dir.Snapshot());
    }

    [Fact]
    public void Reveal_is_refused_again_while_the_reveal_feedback_is_unsaved()
    {
        using var env = EnvWithPastTypoOnA();
        var flow = AtDeferredA(env);
        env.Files.FailWrites = true;
        flow.Reveal();
        Assert.False(flow.As<FeedbackScreen>().Saved);

        Assert.Throws<InvalidOperationException>(flow.Reveal);
    }

    [Fact]
    public void Restart_after_an_unsaved_reveal_resumes_at_the_same_deferred_question_and_reveals_once()
    {
        using var env = EnvWithPastTypoOnA();
        var flow = AtDeferredA(env);
        var wrongBefore = JapaneseRecord(env, A).NormalWrong;
        env.Files.FailWrites = true;
        flow.Reveal();
        env.Files.FailWrites = false; // 保存しないまま再起動

        var restarted = ResumeAt(env, A);

        Assert.Equal(QuestionPhase.Deferred, restarted.As<QuestionScreen>().Phase);
        Assert.Equal(wrongBefore, JapaneseRecord(env, A).NormalWrong);
        restarted.Reveal();
        restarted.RetrySaveIfNeeded();
        Assert.Equal(wrongBefore + 1, JapaneseRecord(env, A).NormalWrong);
        Assert.Equal(PastTypo, JapaneseRecord(env, A).LastWrongInput);
    }

    [Fact]
    public void Revealing_a_word_with_no_past_input_records_no_input()
    {
        using var env = new FlowEnv { WordsJson = Words.Json(new TestWord(A), new TestWord(B)) };
        var flow = env.Start();
        flow.StartPractice(Choices.Manual(Japanese, 2));
        flow.Skip();
        flow.AnswerCurrent(true);
        flow.Next();
        var skipped = flow.As<QuestionScreen>().Question.WordId;

        flow.Reveal();

        var mode = JapaneseRecord(env, skipped);
        Assert.Null(mode.LastWrongInput);
        Assert.Null(mode.LastWrongInputAt);
        Assert.Equal(1, mode.NormalWrong);
        Assert.NotNull(mode.LastWrongAt);
    }

    [Fact]
    public void Flash_deferred_question_allows_reveal_only_after_the_word_is_hidden_and_not_while_its_save_is_pending()
    {
        using var env = new FlowEnv { WordsJson = Words.Json(new TestWord(A), new TestWord(B)) };
        LearningMode[] eligible = [Japanese, Audio, Flash];
        var session = SessionManager.Start(
            SessionId, FlowEnv.T0, [new(A, A, Flash, [.. eligible]), new(B, B, Japanese, [.. eligible])], DefaultSettings.Create());
        env.Dir.Write(ProgressRepository.FileName, ProgressJson.Serialize(new ProgressDocument { WordSetVersion = "v1", SourceVersion = "src1", Session = session }));
        var flow = ResumeAt(env, A);
        flow.RevealFlashIfNeeded();
        flow.Skip();
        flow.AnswerCurrent(true);
        flow.Next();

        // 後回しの A(フラッシュ): 見せる前は答えを見られない
        var notStarted = flow.As<QuestionScreen>();
        Assert.Equal((QuestionPhase.Deferred, FlashPhase.NotStarted), (notStarted.Phase, notStarted.Flash));
        Assert.False(notStarted.CanReveal);
        Assert.Throws<InvalidOperationException>(flow.Reveal);

        // 表示開始の保存に失敗している間も答えを見られない(文書は変わらない)
        var before = env.Dir.Snapshot();
        env.Files.FailWrites = true;
        flow.ShowFlash();
        Assert.True(flow.As<QuestionScreen>().SavePending);
        Assert.False(flow.As<QuestionScreen>().CanReveal);
        Assert.Throws<InvalidOperationException>(flow.Reveal);
        Assert.Equal(before, env.Dir.Snapshot());

        // 表示中も不可。隠した後なら答えを見られる
        env.Files.FailWrites = false;
        flow.RetrySave();
        Assert.Equal(FlashPhase.Showing, flow.As<QuestionScreen>().Flash);
        Assert.False(flow.As<QuestionScreen>().CanReveal);
        Assert.Throws<InvalidOperationException>(flow.Reveal);
        flow.EndFlash();
        Assert.True(flow.As<QuestionScreen>().CanReveal);
        flow.Reveal();
        Assert.True(flow.As<FeedbackScreen>().Revealed);
        Assert.Equal(1, env.Progress().Progress.Word(A)!.Mode(Flash).NormalWrong);
    }

    // ---- 段階別の進捗(UC-21) ----

    [Fact]
    public void Progress_counters_follow_normal_then_deferred_then_retry_including_current_in_remaining()
    {
        using var env = EnvWithPastTypoOnA();
        var flow = ResumeAt(env, A);
        Assert.Equal((QuestionPhase.Normal, 1, 3), Counter(flow));
        flow.Skip();
        Assert.Equal((QuestionPhase.Normal, 2, 3), Counter(flow));
        flow.AnswerCurrent(false);
        flow.Next();
        Assert.Equal((QuestionPhase.Normal, 3, 3), Counter(flow));
        flow.AnswerCurrent(true);
        flow.Next();
        Assert.Equal((QuestionPhase.Deferred, 1, 1), Counter(flow));
        flow.Reveal();
        flow.Next();

        // 再練習は通常問題の順(A: 答えを見る → B: 誤答)。現在問を含む残り 2 → 1
        Assert.Equal((QuestionPhase.Retry, 1, 2), Counter(flow));
        flow.AnswerCurrent(true);
        flow.Next();
        Assert.Equal((QuestionPhase.Retry, 2, 1), Counter(flow));
        flow.AnswerCurrent(false); // 再練習の誤答は回数 1 なので追加されない
        flow.Next();
        Assert.IsType<ResultScreen>(flow.Screen);
    }

    private static (QuestionPhase Phase, int Number, int Total) Counter(PracticeFlow flow)
    {
        var q = flow.As<QuestionScreen>();
        return (q.Phase, q.Number, q.Total);
    }
}

internal static class RevealTestExtensions
{
    /// <summary>判定画面が未保存なら再保存する(失敗を入れていない環境では1回で保存済みになる)。</summary>
    public static void RetrySaveIfNeeded(this PracticeFlow flow)
    {
        if (flow.Screen is FeedbackScreen { Saved: false })
        {
            flow.RetrySave();
        }
    }
}
