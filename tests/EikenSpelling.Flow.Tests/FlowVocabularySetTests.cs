using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;
using EikenSpelling.Persistence;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Flow.Tests;

/// <summary>語彙セットの閲覧(UC-07/08、ST-01〜03/09)。開く・送る・切り替える・戻るでは何も書かない。</summary>
public class FlowVocabularySetBrowseTests
{
    private static string[] Ids(VocabularySetScreen s) => [.. s.Rows.Select(r => r.WordId)];

    [Fact]
    public void ST09_home_opens_the_first_fifty_set_and_writes_nothing()
    {
        using var h = new SetHarness();
        var flow = h.Start();
        var before = h.Env.Dir.Snapshot();

        var set = flow.OpenSets();

        Assert.Equal((50, 1, 3), (set.UnitSize, set.Number, set.Total));
        Assert.Equal(Enumerable.Range(0, 50).Select(SetHarness.Id).ToArray(), Ids(set));
        Assert.False(set.CanPrevious);
        Assert.True(set.CanNext);
        Assert.Equal(before, h.Env.Dir.Snapshot());
        Assert.Empty(h.Issued);
    }

    [Fact]
    public void ST09_browsing_next_previous_unit_switch_and_back_never_write()
    {
        using var h = new SetHarness();
        h.Seed(SetHarness.Weak("waab"));
        var flow = h.Start();
        var before = h.Env.Dir.Snapshot();

        flow.OpenSets();
        flow.NextSet();
        flow.ChangeSetUnit(100);
        flow.ChangeSetUnit(50);
        flow.NextSet();
        flow.PreviousSet();
        flow.CloseVocabularySets();

        Assert.IsType<HomeScreen>(flow.Screen);
        Assert.Equal(before, h.Env.Dir.Snapshot());
        Assert.Empty(h.Issued);
        Assert.False(File.Exists(h.Env.Dir.File(SettingsRepository.FileName)));
    }

    [Fact]
    public void ST02_pages_move_in_order_and_the_last_page_shows_the_remainder()
    {
        using var h = new SetHarness();
        var flow = h.Start();
        flow.OpenSets();

        flow.NextSet();
        var second = flow.As<VocabularySetScreen>();
        flow.NextSet();
        var last = flow.As<VocabularySetScreen>();

        Assert.Equal((2, "wafa"), (second.Number, second.Rows[0].WordId));
        Assert.Equal((3, 20, "wbaa", "wbbj"), (last.Number, last.Rows.Length, last.Rows[0].WordId, last.Rows[^1].WordId));
        Assert.False(last.CanNext);
        Assert.True(last.CanPrevious);
        Assert.Throws<InvalidOperationException>(flow.NextSet);
        flow.PreviousSet();
        Assert.Equal(2, flow.As<VocabularySetScreen>().Number);
    }

    [Fact]
    public void ST02_previous_on_the_first_page_is_rejected_without_changing_the_screen()
    {
        using var h = new SetHarness();
        var flow = h.Start();
        var first = flow.OpenSets();

        Assert.Throws<InvalidOperationException>(flow.PreviousSet);

        Assert.Same(first, flow.Screen);
    }

    [Fact]
    public void ST03_hundred_view_joins_two_adjacent_fifty_sets_and_keeps_the_remainder()
    {
        using var h = new SetHarness();
        var flow = h.Start();
        flow.OpenSets();

        flow.ChangeSetUnit(100);
        var first = flow.As<VocabularySetScreen>();
        flow.NextSet();
        var second = flow.As<VocabularySetScreen>();

        Assert.Equal((100, 1, 2, 100), (first.UnitSize, first.Number, first.Total, first.Rows.Length));
        Assert.Equal(Enumerable.Range(0, 100).Select(SetHarness.Id).ToArray(), Ids(first));
        Assert.Equal((2, 20), (second.Number, second.Rows.Length));
        Assert.False(second.CanNext);
    }

    [Fact]
    public void ST03_unit_switch_shows_the_page_containing_the_original_first_word()
    {
        using var h = new SetHarness(total: 250); // 50語は5セット、100語は3セット
        var flow = h.Start();
        flow.OpenSets();
        flow.NextSet();
        flow.NextSet(); // 50語の3ページ目: w100〜w149

        flow.ChangeSetUnit(100);
        var hundred = flow.As<VocabularySetScreen>();
        flow.ChangeSetUnit(50);
        var back = flow.As<VocabularySetScreen>();

        Assert.Equal(2, hundred.Number); // w100 は100語の2ページ目の先頭
        Assert.Equal("wbaa", hundred.Rows[0].WordId);
        Assert.Equal(3, back.Number);   // 100語の2ページ目の先頭 w100 を含む50語は3ページ目
        Assert.Equal("wbaa", back.Rows[0].WordId);
    }

    [Fact]
    public void Unit_switch_from_the_second_fifty_set_lands_on_the_first_hundred_set_then_returns_to_the_first_fifty()
    {
        using var h = new SetHarness(total: 250);
        var flow = h.Start();
        flow.OpenSets();
        flow.NextSet(); // w050〜

        flow.ChangeSetUnit(100);
        var hundred = flow.As<VocabularySetScreen>();
        flow.ChangeSetUnit(50);

        Assert.Equal(1, hundred.Number);
        Assert.Equal(1, flow.As<VocabularySetScreen>().Number); // 先頭が w000 になるので元のページには戻らない(先頭の語を含むページ)
    }

    [Fact]
    public void Unsupported_unit_is_rejected_without_changing_the_screen()
    {
        using var h = new SetHarness();
        var flow = h.Start();
        var set = flow.OpenSets();

        Assert.Throws<ArgumentOutOfRangeException>(() => flow.ChangeSetUnit(75));

        Assert.Same(set, flow.Screen);
    }

    [Fact]
    public void ST09_each_row_shows_the_learning_state_from_the_word_id_record()
    {
        using var h = new SetHarness();
        h.Seed(SetHarness.Weak("waab"), SetHarness.DueReview("waac"), SetHarness.Normal("waad"));
        var flow = h.Start();

        var set = flow.OpenSets();

        Assert.Equal(WordStudyState.Unseen, set.Rows[0].State);
        Assert.Equal(WordStudyState.Weak, set.Rows[1].State);
        Assert.Equal(WordStudyState.ReviewDue, set.Rows[2].State);
        Assert.Equal(WordStudyState.Normal, set.Rows[3].State);
        // 50語表示でも100語表示でも同じ WordId の状態は同じ(セット別の成績は無い)
        flow.ChangeSetUnit(100);
        var hundred = flow.As<VocabularySetScreen>();
        Assert.Equal(set.Rows.Select(r => r.State).ToArray(), hundred.Rows.Take(50).Select(r => r.State).ToArray());
    }

    [Fact]
    public void Reopening_keeps_the_unit_and_page_during_the_same_run()
    {
        using var h = new SetHarness();
        var flow = h.Start();
        flow.OpenSets();
        flow.ChangeSetUnit(100);
        flow.NextSet();
        flow.CloseVocabularySets();

        var reopened = flow.OpenSets();

        Assert.Equal((100, 2), (reopened.UnitSize, reopened.Number));
    }

    [Fact]
    public void Set_operations_are_rejected_from_other_screens()
    {
        using var h = new SetHarness();
        var flow = h.Start();

        Assert.Throws<InvalidOperationException>(flow.NextSet);
        Assert.Throws<InvalidOperationException>(flow.CloseVocabularySets);
        Assert.Throws<InvalidOperationException>(() => flow.PreviewSetPractice(Choices.Auto()));
        Assert.Throws<InvalidOperationException>(flow.ConfirmSetStart);
        Assert.Throws<InvalidOperationException>(flow.CancelSetStart);
        flow.StartPractice(Choices.Auto());
        Assert.Throws<InvalidOperationException>(flow.OpenVocabularySets);
    }

    [Fact]
    public void The_set_screen_carries_the_start_conditions_from_the_saved_defaults()
    {
        using var h = new SetHarness();
        h.Env.Dir.Write(SettingsRepository.FileName, """{ "settingsVersion": 1, "practice": { "questionCount": 20 } }""");
        var flow = h.Start();

        var set = flow.OpenSets();

        Assert.Equal(20, set.Choice.QuestionCount);
        Assert.NotEmpty(set.QuestionCountChoices);
        Assert.NotEmpty(set.FlashSecondsChoices);
    }
}

/// <summary>セット限定の開始・不足確認・同じ Plan での開始(UC-09、ST-04〜06、AC-X02/03/04/07)。</summary>
public class FlowVocabularySetStartTests
{
    /// <summary>セット1(w000〜w049)のうち穴あきが使えるのは w000〜w007 の8語だけ。セット外は全部使える。</summary>
    private static SetHarness EightEligibleInSetOne()
    {
        var h = new SetHarness(hasBlank: i => i < 8 || i >= 50);
        h.Seed(SetHarness.Weak("waga"), SetHarness.DueReview("wagb"), SetHarness.Weak("waac", mistakes: 1));
        return h;
    }

    private static PracticeChoice BlankChoice(int count = 20) => Choices.Manual(Blank, count);

    [Fact]
    public void AC_X03_short_candidates_show_desired_and_available_and_do_not_write_before_confirmation()
    {
        using var h = EightEligibleInSetOne();
        var flow = h.Start();
        flow.OpenSets();
        var before = h.Env.Dir.Snapshot();

        flow.PreviewSetPractice(BlankChoice(20));

        var preview = flow.As<SetStartPreviewScreen>();
        Assert.Equal((20, 8), (preview.Desired, preview.Available));
        Assert.True(preview.IsShort);
        Assert.True(preview.CanStart);
        Assert.False(preview.Stale);
        Assert.Equal(8, preview.Plan.Length);
        Assert.All(preview.Plan, q => Assert.Contains(q.WordId, Enumerable.Range(0, 8).Select(SetHarness.Id)));
        Assert.Equal(before, h.Env.Dir.Snapshot());
        Assert.Empty(h.Issued);
    }

    [Fact]
    public void AC_X03_cancel_returns_to_the_same_set_page_with_the_same_conditions_and_writes_nothing()
    {
        using var h = EightEligibleInSetOne();
        var flow = h.Start();
        flow.OpenSets();
        flow.PreviewSetPractice(BlankChoice(20));
        var before = h.Env.Dir.Snapshot();

        flow.CancelSetStart();

        var set = flow.As<VocabularySetScreen>();
        Assert.Equal((50, 1), (set.UnitSize, set.Number));
        Assert.Equal(BlankChoice(20), set.Choice);
        Assert.Equal(before, h.Env.Dir.Snapshot());
        Assert.Empty(h.Issued);
        Assert.Throws<InvalidOperationException>(flow.ConfirmSetStart); // 取り消した確認は使えない
    }

    [Fact]
    public void AC_X02_selection_never_includes_words_outside_the_set_even_high_priority_weak_and_due_review()
    {
        using var h = EightEligibleInSetOne();
        var flow = h.Start();
        flow.OpenSets();

        flow.PreviewSetPractice(BlankChoice(20));

        var ids = flow.As<SetStartPreviewScreen>().Plan.Select(q => q.WordId).ToArray();
        Assert.DoesNotContain("waga", ids);
        Assert.DoesNotContain("wagb", ids);
        Assert.Contains("waac", ids); // セット内の苦手は出る
    }

    [Fact]
    public void AC_X03_confirm_starts_the_same_plan_in_the_same_order_and_modes_without_rerunning_the_selector()
    {
        using var h = EightEligibleInSetOne();
        var flow = h.Start();
        flow.OpenSets();
        flow.PreviewSetPractice(BlankChoice(20));
        var plan = flow.As<SetStartPreviewScreen>().Plan.Select(q => (q.WordId, q.Mode)).ToArray();
        var randomCalls = h.Random.Calls;

        flow.ConfirmSetStart();

        Assert.IsType<QuestionScreen>(flow.Screen);
        Assert.Equal(randomCalls, h.Random.Calls); // Selector(乱数を使う)を撃ち直していない
        var saved = h.SavedSession()!;
        Assert.Equal(plan, saved.Questions.Where(q => q.Attempt == AttemptType.Normal).Select(q => (q.WordId, q.Mode)).ToArray());
        Assert.Equal(Assert.Single(h.Issued), saved.SessionId);
        Assert.Equal(1, saved.Scope!.Number);
        Assert.Equal(50, saved.Scope.UnitSize);
        Assert.Equal(Enumerable.Range(0, 50).Select(SetHarness.Id).ToArray(), saved.Scope.WordIds.ToArray());
        Assert.Equal(20, saved.Settings.Practice.QuestionCount); // 希望の問題数を設定の写しに残す
    }

    [Fact]
    public void AC_X03_double_confirm_creates_only_one_session()
    {
        using var h = EightEligibleInSetOne();
        var flow = h.Start();
        flow.OpenSets();
        flow.PreviewSetPractice(BlankChoice(20));
        flow.ConfirmSetStart();

        Assert.Throws<InvalidOperationException>(flow.ConfirmSetStart);

        Assert.Single(h.Issued);
        Assert.IsType<QuestionScreen>(flow.Screen);
    }

    [Fact]
    public void AC_X04_start_save_failure_keeps_the_screen_and_retry_uses_the_same_session_id_and_plan()
    {
        using var h = EightEligibleInSetOne();
        var flow = h.Start();
        flow.OpenSets();
        flow.PreviewSetPractice(BlankChoice(20));
        var plan = flow.As<SetStartPreviewScreen>().Plan.Select(q => (q.WordId, q.Mode)).ToArray();
        var before = h.Env.Dir.Snapshot();
        h.Env.Files.FailWrites = true;

        flow.ConfirmSetStart();
        var failed = flow.As<SetStartPreviewScreen>();
        flow.ConfirmSetStart(); // 二重クリック
        var failedAgain = flow.As<SetStartPreviewScreen>();

        Assert.NotNull(failed.SaveFailure);
        Assert.NotNull(failed.Diagnostic);
        Assert.DoesNotContain(h.Env.Dir.Path, failed.Diagnostic, StringComparison.Ordinal);
        Assert.NotNull(failedAgain.SaveFailure);
        Assert.Equal(before, h.Env.Dir.Snapshot());
        Assert.Single(h.Issued);

        h.Env.Files.FailWrites = false;
        flow.ConfirmSetStart();

        Assert.IsType<QuestionScreen>(flow.Screen);
        Assert.Single(h.Issued);
        var saved = h.SavedSession()!;
        Assert.Equal(h.Issued[0], saved.SessionId);
        Assert.Equal(plan, saved.Questions.Where(q => q.Attempt == AttemptType.Normal).Select(q => (q.WordId, q.Mode)).ToArray());
        Assert.Equal(plan[0].WordId, flow.As<QuestionScreen>().Question.WordId);
    }

    [Fact]
    public void AC_X04_zero_candidates_show_a_reason_cannot_start_and_write_nothing()
    {
        using var h = new SetHarness(hasBlank: i => i >= 50); // セット1に穴あきの語が無い
        var flow = h.Start();
        flow.OpenSets();
        var before = h.Env.Dir.Snapshot();

        flow.PreviewSetPractice(BlankChoice(10));

        var preview = flow.As<SetStartPreviewScreen>();
        Assert.Equal((10, 0), (preview.Desired, preview.Available));
        Assert.False(preview.CanStart);
        Assert.NotEqual(SelectionEmptyReason.None, preview.EmptyReason);
        Assert.Throws<InvalidOperationException>(flow.ConfirmSetStart);
        Assert.Equal(before, h.Env.Dir.Snapshot());
        Assert.Empty(h.Issued);
        flow.CancelSetStart();
        Assert.IsType<VocabularySetScreen>(flow.Screen);
    }

    [Fact]
    public void ST10_weak_only_with_no_weak_words_in_the_set_cannot_start_even_if_weak_words_exist_outside()
    {
        using var h = new SetHarness();
        h.Seed(SetHarness.Weak("waga"));
        var flow = h.Start();
        flow.OpenSets();

        flow.PreviewSetPractice(Choices.Auto(10, weakOnly: true));

        var preview = flow.As<SetStartPreviewScreen>();
        Assert.False(preview.CanStart);
        Assert.Equal(SelectionEmptyReason.NoWeakWords, preview.EmptyReason);
    }

    [Fact]
    public void AC_X04_full_count_starts_without_an_extra_confirmation()
    {
        using var h = new SetHarness();
        var flow = h.Start();
        flow.OpenSets();

        flow.PreviewSetPractice(Choices.Auto(10));

        var question = flow.As<QuestionScreen>();
        var saved = h.SavedSession()!;
        Assert.Equal(10, saved.NormalCount);
        Assert.NotNull(saved.Scope);
        Assert.All(saved.Questions, q => Assert.Contains(q.WordId, saved.Scope.WordIds));
        Assert.Equal(saved.Questions[0].WordId, question.Question.WordId);
        Assert.Single(h.Issued);
    }

    [Fact]
    public void AC_X04_full_count_save_failure_offers_the_same_plan_for_retry_and_creates_one_session()
    {
        using var h = new SetHarness();
        var flow = h.Start();
        flow.OpenSets();
        h.Env.Files.FailWrites = true;

        flow.PreviewSetPractice(Choices.Auto(10));

        var preview = flow.As<SetStartPreviewScreen>();
        Assert.False(preview.IsShort);
        Assert.NotNull(preview.SaveFailure);
        var plan = preview.Plan.Select(q => q.WordId).ToArray();
        h.Env.Files.FailWrites = false;
        flow.ConfirmSetStart();
        Assert.Single(h.Issued);
        Assert.Equal(plan, h.SavedSession()!.Questions.Select(q => q.WordId).Take(10).ToArray());
    }

    [Fact]
    public void AC_X07_changed_mode_availability_expires_the_old_confirmation_and_asks_again()
    {
        using var h = EightEligibleInSetOne();
        var flow = h.Start();
        flow.OpenSets();
        flow.PreviewSetPractice(Choices.Auto(60)); // セット1は50語しかないので不足
        var old = flow.As<SetStartPreviewScreen>();
        Assert.Contains(old.Plan, q => q.Mode == Audio);
        h.Env.Modes.Remove(Audio); // 音声が使えなくなった
        var before = h.Env.Dir.Snapshot();

        flow.ConfirmSetStart();

        var again = flow.As<SetStartPreviewScreen>();
        Assert.True(again.Stale);
        Assert.DoesNotContain(again.Plan, q => q.Mode == Audio);
        Assert.Equal(before, h.Env.Dir.Snapshot());
        Assert.Empty(h.Issued);

        flow.ConfirmSetStart(); // 新しい確認に同意して開始

        Assert.IsType<QuestionScreen>(flow.Screen);
        Assert.Single(h.Issued);
        Assert.DoesNotContain(h.SavedSession()!.Questions, q => q.Mode == Audio);
    }

    [Fact]
    public void Starting_from_a_set_is_not_carried_over_to_recommended_free_choice_or_weak_review()
    {
        using var h = EightEligibleInSetOne();
        var flow = h.Start();
        flow.OpenSets();
        flow.PreviewSetPractice(BlankChoice(20));
        flow.ConfirmSetStart();
        flow.AnswerAll(_ => true);
        flow.GoHome();

        flow.StartRecommended();
        var recommended = h.SavedSession()!;
        Assert.Null(recommended.Scope);
        Assert.Contains(recommended.Questions, q => q.WordId.CompareTo("wafa") >= 0); // セット外も出る
        flow.Quit();
        flow.DiscardAndGoHome();

        flow.StartPractice(Choices.Auto(10));
        Assert.Null(h.SavedSession()!.Scope);
        flow.Quit();
        flow.DiscardAndGoHome();

        flow.OpenWeakList();
        flow.StartWeakPractice(Choices.Auto(10));
        Assert.Null(h.SavedSession()!.Scope);
    }

    [Fact]
    public void Set_start_does_not_change_the_saved_basic_settings()
    {
        using var h = EightEligibleInSetOne();
        var flow = h.Start();
        flow.OpenSets();
        flow.PreviewSetPractice(BlankChoice(20));
        flow.ConfirmSetStart();

        Assert.False(File.Exists(h.Env.Dir.File(SettingsRepository.FileName)));
    }

    [Fact]
    public void Second_page_scope_uses_that_pages_unit_number_and_total()
    {
        using var h = new SetHarness();
        var flow = h.Start();
        flow.OpenSets();
        flow.ChangeSetUnit(100);
        flow.NextSet(); // 100語の2ページ目(20語)

        flow.PreviewSetPractice(Choices.Auto(10));

        var scope = h.SavedSession()!.Scope!;
        Assert.Equal((100, 2, 2), (scope.UnitSize, scope.Number, scope.TotalSets));
        Assert.Equal(20, scope.WordIds.Length);
        Assert.All(h.SavedSession()!.Questions, q => Assert.True(string.CompareOrdinal(q.WordId, "wbaa") >= 0));
    }
}

/// <summary>セット開始の境界・失敗の経路(エッジケースのレビュー: 保存失敗と取消・失効の組み合わせ、希望数の境目)。</summary>
public class FlowVocabularySetEdgeTests
{
    private static SetHarness EightEligible()
    {
        var h = new SetHarness(hasBlank: i => i < 8 || i >= 50);
        h.Seed(SetHarness.Weak(SetHarness.Id(60)), SetHarness.DueReview(SetHarness.Id(61)));
        return h;
    }

    private static PracticeChoice BlankChoice(int count) => Choices.Manual(Blank, count);

    [Fact]
    public void An_invalid_choice_is_rejected_without_changing_the_remembered_conditions()
    {
        using var h = EightEligible();
        var flow = h.Start();
        var set = flow.OpenSets();

        Assert.ThrowsAny<ArgumentException>(() => flow.PreviewSetPractice(Choices.Auto(0)));

        Assert.Same(set, flow.Screen);
        Assert.Equal(set.Choice, flow.As<VocabularySetScreen>().Choice);
        flow.NextSet();
        Assert.Equal(set.Choice, flow.As<VocabularySetScreen>().Choice); // ページ送りでも不正な値は残らない
    }

    [Fact]
    public void Start_conditions_are_this_time_only_and_reset_each_time_the_sets_are_opened()
    {
        using var h = EightEligible();
        var flow = h.Start();
        flow.OpenSets();
        flow.PreviewSetPractice(BlankChoice(20));
        flow.CancelSetStart();
        Assert.Equal(BlankChoice(20), flow.As<VocabularySetScreen>().Choice);
        flow.CloseVocabularySets();

        var reopened = flow.OpenSets();

        Assert.NotEqual(BlankChoice(20), reopened.Choice); // 保存済みの基本設定に戻る
    }

    [Fact]
    public void Desired_one_above_available_asks_for_confirmation_and_exactly_available_starts_directly()
    {
        using var h = EightEligible();
        var flow = h.Start();
        flow.OpenSets();

        flow.PreviewSetPractice(BlankChoice(9));
        var preview = flow.As<SetStartPreviewScreen>();
        Assert.Equal((9, 8, true), (preview.Desired, preview.Available, preview.IsShort));
        flow.CancelSetStart();

        flow.PreviewSetPractice(BlankChoice(8));

        Assert.IsType<QuestionScreen>(flow.Screen);
        Assert.Single(h.Issued);
    }

    [Fact]
    public void Save_failure_then_cancel_then_a_new_preview_starts_one_session_with_a_new_id()
    {
        using var h = EightEligible();
        var flow = h.Start();
        flow.OpenSets();
        flow.PreviewSetPractice(BlankChoice(20));
        h.Env.Files.FailWrites = true;
        flow.ConfirmSetStart();
        flow.CancelSetStart(); // 取り消すと失敗していた候補は捨てる
        h.Env.Files.FailWrites = false;

        flow.PreviewSetPractice(BlankChoice(20));
        flow.ConfirmSetStart();

        Assert.Equal(2, h.Issued.Count);
        Assert.Equal(h.Issued[1], h.SavedSession()!.SessionId);
    }

    [Fact]
    public void Save_failure_then_a_changed_premise_asks_again_and_the_retry_after_that_creates_one_saved_session()
    {
        using var h = EightEligible();
        var flow = h.Start();
        flow.OpenSets();
        flow.PreviewSetPractice(Choices.Auto(60));
        h.Env.Files.FailWrites = true;
        flow.ConfirmSetStart();
        Assert.NotNull(flow.As<SetStartPreviewScreen>().SaveFailure);
        h.Env.Modes.Remove(Audio);
        h.Env.Files.FailWrites = false;

        flow.ConfirmSetStart();
        Assert.True(flow.As<SetStartPreviewScreen>().Stale);
        flow.ConfirmSetStart();

        Assert.IsType<QuestionScreen>(flow.Screen);
        var saved = h.SavedSession()!;
        Assert.Equal(h.Issued[^1], saved.SessionId);
        Assert.DoesNotContain(saved.Questions, q => q.Mode == Audio);
        Assert.Equal(2, h.Issued.Count); // 失敗していた古い候補は使われず、新しい計画の1つだけが保存される
    }

    [Fact]
    public void Switching_to_100_from_the_last_fifty_page_lands_on_the_page_that_contains_its_first_word()
    {
        using var h = new SetHarness(); // 120語: 50語は3ページ(最後は20語)、100語は2ページ
        var flow = h.Start();
        flow.OpenSets();
        flow.NextSet();
        flow.NextSet();

        flow.ChangeSetUnit(100);
        var hundred = flow.As<VocabularySetScreen>();

        Assert.Equal((100, 2, 20), (hundred.UnitSize, hundred.Number, hundred.Rows.Length));
    }

    [Fact]
    public void Choosing_the_unit_already_shown_keeps_the_same_page()
    {
        using var h = new SetHarness();
        var flow = h.Start();
        flow.OpenSets();
        flow.NextSet();

        flow.ChangeSetUnit(50);

        Assert.Equal((50, 2), (flow.As<VocabularySetScreen>().UnitSize, flow.As<VocabularySetScreen>().Number));
    }
}
