using System.Text.Json.Nodes;

using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;
using EikenSpelling.Persistence;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Flow.Tests;

/// <summary>
/// 語彙セットの通し(最優先の5段)・中断/再開・語彙更新・旧データ(UC-09/10、AC-X02/03/04/07/12/14、ST-07/08)。
/// 本物の Flow と一時フォルダの本物のファイルで確かめる(保存失敗だけ差し替える)。
/// </summary>
public class FlowVocabularySetAcceptanceTests
{
    private static readonly string[] SetOneIds = [.. Enumerable.Range(0, 50).Select(SetHarness.Id)];

    /// <summary>セット1の穴あきが使える語は8語だけ。セット外に期限が来た再確認(w061)と高優先の苦手(w060)を置く。</summary>
    private static SetHarness Scenario()
    {
        var h = new SetHarness(hasBlank: i => i < 8 || i >= 50);
        h.Seed(SetHarness.Weak("waga"), SetHarness.DueReview("wagb"), SetHarness.Weak("waac", mistakes: 1), SetHarness.DueReview("waae"));
        return h;
    }

    [Fact]
    public void Five_stage_home_to_set_preview_confirm_fail_retry_skip_wrong_restart_resume_and_ordinary_study_afterwards()
    {
        using var h = Scenario();
        var seeded = h.Env.Progress().Progress;
        var progressBefore = h.ProgressText;
        var flow = h.Start();

        // 1・2: 範囲外の再確認・苦手を置いた状態でセットを開き、20問希望・8問可能。確認前も取消後も学習記録は不変
        flow.OpenVocabularySets();
        flow.PreviewSetPractice(Choices.Manual(Blank, 20));
        var preview = flow.As<SetStartPreviewScreen>();
        Assert.Equal((20, 8), (preview.Desired, preview.Available));
        Assert.Equal(progressBefore, h.ProgressText);
        flow.CancelSetStart();
        Assert.Equal(progressBefore, h.ProgressText);
        Assert.Empty(h.Issued);

        // 3: 再度プレビュー → 今回の Plan を記録して同意。開始保存を1回失敗させ、再試行後も同じ8問・順序・モード・セッション1つ
        flow.PreviewSetPractice(Choices.Manual(Blank, 20));
        var plan = flow.As<SetStartPreviewScreen>().Plan.Select(q => (q.WordId, q.Mode)).ToArray();
        Assert.Equal(8, plan.Length);
        h.Env.Files.FailWrites = true;
        flow.ConfirmSetStart();
        Assert.NotNull(flow.As<SetStartPreviewScreen>().SaveFailure);
        Assert.Equal(progressBefore, h.ProgressText);
        h.Env.Files.FailWrites = false;
        flow.ConfirmSetStart();
        var started = h.SavedSession()!;
        Assert.Single(h.Issued);
        Assert.Equal(h.Issued[0], started.SessionId);
        Assert.Equal(plan, started.Questions.Select(q => (q.WordId, q.Mode)).ToArray());
        Assert.All(plan, p => Assert.Contains(p.WordId, SetOneIds));

        // 4: スキップと誤答を起こして中断・再起動。元の Plan の正しい位置から再開し、後回し・再練習を含めて範囲外は0件
        var asked = new List<string>();
        flow.Skip();
        asked.Add(plan[0].WordId);
        flow.AnswerCurrent(false);
        flow.Next();
        flow.AnswerCurrent(true);
        flow.Next();
        var expectedNext = flow.As<QuestionScreen>().Question;
        var expectedNumber = flow.As<QuestionScreen>().Number;

        var restarted = h.Start();
        var prompt = restarted.As<ResumePromptScreen>();
        Assert.True(prompt.CanResume);
        Assert.NotNull(prompt.Session.Set);
        Assert.Equal((50, 1, 3), (prompt.Session.Set!.UnitSize, prompt.Session.Set.Number, prompt.Session.Set.Total));
        restarted.Resume();
        Assert.Equal(expectedNext, restarted.As<QuestionScreen>().Question);
        Assert.Equal(expectedNumber, restarted.As<QuestionScreen>().Number);
        Assert.Equal(2, restarted.As<QuestionScreen>().Answered);
        Assert.Equal(plan, h.SavedSession()!.Questions.Take(8).Select(q => (q.WordId, q.Mode)).ToArray());

        var phases = new HashSet<QuestionPhase>();
        while (restarted.Screen is QuestionScreen q)
        {
            phases.Add(q.Phase);
            asked.Add(q.Question.WordId);
            restarted.AnswerCurrent(q.Question.IsRetry);
            restarted.Next();
        }

        Assert.IsType<ResultScreen>(restarted.Screen);
        Assert.Contains(QuestionPhase.Deferred, phases);
        Assert.Contains(QuestionPhase.Retry, phases);
        Assert.All(asked, id => Assert.Contains(id, SetOneIds));
        Assert.Null(h.SavedSession());

        // セット外の記録は一切変わらない(成績の正本は WordId 別の記録のまま)
        var after = h.Env.Progress().Progress;
        foreach (var outside in new[] { "waga", "wagb" })
        {
            Assert.Equal(seeded.Word(outside)!.State, after.Word(outside)!.State);
            Assert.Equal(seeded.Word(outside)!.LastNormalAskedAt, after.Word(outside)!.LastNormalAskedAt);
            Assert.Equal(seeded.Word(outside)!.Modes.Values.Sum(m => m.NormalWrong), after.Word(outside)!.Modes.Values.Sum(m => m.NormalWrong));
        }

        // 5: 完了後にホームから通常のおすすめ学習。範囲の持ち越しがなく、固定乱数・固定時刻の従来の選出と一致
        restarted.GoHome();
        var fresh = h.Env.Start(); // 固定シードの乱数・固定時刻
        fresh.StartRecommended();
        var recommended = h.SavedSession()!;
        Assert.Null(recommended.Scope);
        var wordSet = WordSetLoader.Parse(h.Env.WordsJson);
        var expected = QuestionSelector.Select(
            wordSet, h.Env.Progress().Progress, recommended.Settings, h.Env.Modes, FlowEnv.T0, new Random(1));
        Assert.Equal(
            expected.Plan.Select(p => (p.WordId, p.Mode)).ToArray(),
            recommended.Questions.Where(x => x.Attempt == AttemptType.Normal).Select(x => (x.WordId, x.Mode)).ToArray());
        Assert.Contains(recommended.Questions, x => string.CompareOrdinal(x.WordId, "wafa") >= 0);
    }

    [Fact]
    public void AC_X12_resume_keeps_the_saved_plan_after_a_word_update_reorders_the_sets_and_tells_the_user()
    {
        using var h = new SetHarness();
        var flow = h.Start();
        flow.OpenSets();
        flow.PreviewSetPractice(Choices.Auto(10));
        flow.AnswerCurrent(true);
        flow.Next();
        var savedQuestions = h.SavedSession()!.Questions.Select(q => (q.WordId, q.Mode)).ToArray();
        var expectedNext = flow.As<QuestionScreen>().Question;

        // 語彙更新: Tier 1 の新しい語が先頭に入り、セット1の中身が総入れ替えになる
        var added = Enumerable.Range(0, 30).Select(i => new TestWord(SetHarness.Enc($"n{i:D3}"), Tier: 1)).ToArray();
        h.Env.WordsJson = Words.Json("v2", [.. added, .. SetHarness.Make(120)]);
        var after = h.Start();

        Assert.Contains(after.Screen.Notices, n => n.Contains("更新"));
        var prompt = after.As<ResumePromptScreen>();
        Assert.True(prompt.Session.Set!.WordSetUpdated);
        Assert.Equal((50, 1, 3), (prompt.Session.Set.UnitSize, prompt.Session.Set.Number, prompt.Session.Set.Total));
        after.Resume();
        Assert.Equal(expectedNext, after.As<QuestionScreen>().Question);
        Assert.Equal(savedQuestions, h.SavedSession()!.Questions.Select(q => (q.WordId, q.Mode)).ToArray()); // 再抽選しない

        after.AnswerAll(_ => true);
        after.GoHome();
        var reorganized = after.OpenSets();
        Assert.Equal("naaa", reorganized.Rows[0].WordId); // 一覧は新しいデータで再編される
        Assert.Equal(3, reorganized.Total);
    }

    [Fact]
    public void ST08_a_word_update_notice_is_shown_once_not_on_a_normal_restart()
    {
        using var h = new SetHarness();
        var first = h.Start();
        first.StartPractice(Choices.Auto(5));
        first.Quit();
        first.DiscardAndGoHome();

        var same = h.Start();
        Assert.DoesNotContain(same.Screen.Notices, n => n.Contains("更新"));

        h.Env.WordsJson = Words.Json("v2", SetHarness.Make(120));
        var updated = h.Start();
        Assert.Contains(updated.Screen.Notices, n => n.Contains("更新"));
    }

    [Fact]
    public void ST07_resume_is_refused_with_a_reason_when_a_saved_word_is_gone_and_discard_protects_the_records()
    {
        using var h = Scenario();
        var flow = h.Start();
        flow.OpenSets();
        flow.PreviewSetPractice(Choices.Manual(Blank, 20));
        flow.ConfirmSetStart();
        flow.AnswerCurrent(false);
        flow.Next();
        var planned = h.SavedSession()!.Questions.Select(q => q.WordId).ToArray();
        h.Env.WordsJson = Words.Json("v2", [.. SetHarness.Make(120).Where(w => w.Id != planned[3])]); // 出題予定の語が消えた

        var after = h.Start();

        var prompt = after.As<ResumePromptScreen>();
        Assert.False(prompt.CanResume);
        Assert.NotNull(prompt.CannotResumeReason);
        Assert.Throws<InvalidOperationException>(after.Resume);
        var wrongCountBefore = h.Env.Progress().Progress.Word(planned[0])!.Modes.Values.Sum(m => m.NormalWrong);

        after.DiscardAndGoHome();

        Assert.IsType<HomeScreen>(after.Screen);
        Assert.Null(h.SavedSession());
        Assert.Equal(wrongCountBefore, h.Env.Progress().Progress.Word(planned[0])!.Modes.Values.Sum(m => m.NormalWrong)); // 成績は残る
        Assert.NotNull(h.Env.Progress().Progress.Word("waga"));
    }

    [Fact]
    public void AC_X14_a_saved_session_with_a_question_outside_its_scope_is_rejected_on_load_and_records_are_kept()
    {
        using var h = Scenario();
        var flow = h.Start();
        flow.OpenSets();
        flow.PreviewSetPractice(Choices.Manual(Blank, 20));
        flow.ConfirmSetStart();
        var root = JsonNode.Parse(h.ProgressText!)!.AsObject();
        root["session"]!["scope"]!["wordIds"] = new JsonArray(SetHarness.Id(0), SetHarness.Id(1)); // 出題予定の語の多くが範囲外
        h.Env.Dir.Write(ProgressRepository.FileName, root.ToJsonString());
        var filesBefore = h.Env.Dir.Names().Length;

        var after = h.Start();

        Assert.IsType<HomeScreen>(after.Screen); // 再開の選択にならない(セッションだけ捨てた)
        Assert.Contains(after.Screen.Notices, n => n.Contains("復元できませんでした"));
        Assert.True(h.Env.Dir.Names().Length > filesBefore); // 元のファイルの写しが残る
        Assert.NotNull(h.Env.Progress().Progress.Word("waga")); // 学習記録は読めている
    }

    [Fact]
    public void A_session_started_without_a_set_has_no_scope_and_resumes_without_any_set_restriction()
    {
        using var h = new SetHarness();
        var flow = h.Start();
        flow.StartPractice(Choices.Auto(10));
        flow.AnswerCurrent(true);
        flow.Next();
        Assert.False(JsonNode.Parse(h.ProgressText!)!["session"]!.AsObject().ContainsKey("scope"));

        var after = h.Start();

        var prompt = after.As<ResumePromptScreen>();
        Assert.Null(prompt.Session.Set);
        Assert.True(prompt.CanResume);
        after.Resume();
        Assert.IsType<QuestionScreen>(after.Screen);
        Assert.IsType<ResultScreen>(after.AnswerAll(_ => true));
    }

    [Fact]
    public void A_schema_v1_session_without_skips_or_scope_still_resumes_and_is_not_given_an_empty_scope()
    {
        using var h = new SetHarness();
        var flow = h.Start();
        flow.StartPractice(Choices.Auto(10));
        flow.AnswerCurrent(true);
        flow.Next();
        var root = JsonNode.Parse(h.ProgressText!)!.AsObject();
        root["schemaVersion"] = 1;
        root["session"]!.AsObject().Remove("skips");
        h.Env.Dir.Write(ProgressRepository.FileName, root.ToJsonString());

        var after = h.Start();

        var prompt = after.As<ResumePromptScreen>();
        Assert.True(prompt.CanResume);
        Assert.Null(prompt.Session.Set);
        after.Resume();
        Assert.Equal(2, after.As<QuestionScreen>().Number);
    }

    [Fact]
    public void Starting_a_set_from_a_second_page_after_a_normal_session_uses_only_that_pages_words()
    {
        using var h = new SetHarness();
        var flow = h.Start();
        flow.StartPractice(Choices.Auto(10));
        flow.Quit();
        flow.DiscardAndGoHome();
        flow.OpenSets();
        flow.NextSet();

        flow.PreviewSetPractice(Choices.Auto(10));

        Assert.All(h.SavedSession()!.Questions, q => Assert.InRange(string.CompareOrdinal(q.WordId, "wafa"), 0, int.MaxValue));
        Assert.All(h.SavedSession()!.Questions, q => Assert.True(string.CompareOrdinal(q.WordId, "wbaa") < 0));
    }

    [Fact]
    public void The_resume_summary_names_the_phase_of_the_next_question()
    {
        using var h = new SetHarness();
        var flow = h.Start();
        flow.OpenSets();
        flow.PreviewSetPractice(Choices.Auto(5));
        flow.Skip();

        var after = h.Start();

        Assert.Equal(QuestionPhase.Normal, after.As<ResumePromptScreen>().Session.Phase);
        Assert.Equal(1, after.As<ResumePromptScreen>().Session.Skipped);
    }
}
