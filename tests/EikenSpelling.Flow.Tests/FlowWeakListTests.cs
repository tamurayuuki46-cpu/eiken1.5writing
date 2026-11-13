using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;
using EikenSpelling.Persistence;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Flow.Tests;

public class FlowWeakListTests
{
    private static readonly TimeSpan Jst9 = TimeSpan.FromHours(9);

    private static DateTimeOffset Day(int day, int hour = 10, int minute = 0, int second = 0) =>
        new(2026, 11, day, hour, minute, second, Jst9);

    /// <summary>単語を1つずつの手動(日本語)練習で誤答させ、苦手にする(再練習は正答)。</summary>
    private static void MakeWeak(PracticeFlow flow, int count)
    {
        flow.StartPractice(Choices.Manual(Japanese, count));
        flow.AnswerAll(q => q.IsRetry);
        flow.GoHome();
    }

    /// <summary>苦手単語を1日1回ずつ正答させる(一覧から苦手のみ練習)。</summary>
    private static void CorrectWeakOnce(FlowEnv env, PracticeFlow flow, DateTimeOffset at, int count = 1)
    {
        env.Time.Now = at;
        flow.OpenWeakList();
        flow.StartWeakPractice(Choices.Manual(Japanese, count));
        flow.AnswerAll(_ => true);
        flow.GoHome();
    }

    [Fact]
    public void First_run_home_shows_every_word_as_unseen()
    {
        using var env = new FlowEnv();

        var home = env.Start().As<HomeScreen>();

        Assert.Equal(new StudyStatus(5, 0, 0, 0, 0), home.Status);
    }

    [Fact]
    public void Home_status_after_practice_counts_weak_and_normal_words()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.StartPractice(Choices.Manual(Japanese, 3));

        var n = 0;
        flow.AnswerAll(q => ++n != 1 || q.IsRetry); // 通常の1問目だけ誤答(再練習は正答)
        flow.GoHome();

        Assert.Equal(new StudyStatus(Unseen: 2, Normal: 2, Weak: 1, Mastered: 0, ReviewDue: 0), flow.As<HomeScreen>().Status);
    }

    [Fact]
    public void Weak_practice_from_list_masters_word_and_home_reflects_it()
    {
        using var env = new FlowEnv { WordsJson = Words.Json(new TestWord("apple"), new TestWord("lemon")) };
        env.Time.Now = Day(1);
        var flow = env.Start();
        MakeWeak(flow, 1);
        var weakId = env.Progress().Progress.Words.Keys.Single();

        CorrectWeakOnce(env, flow, Day(2));
        CorrectWeakOnce(env, flow, Day(3)); // 必要回数 2(既定)で克服

        Assert.Equal(new StudyStatus(Unseen: 1, Normal: 0, Weak: 0, Mastered: 1, ReviewDue: 0), flow.As<HomeScreen>().Status);
        flow.OpenWeakList();
        var row = Assert.Single(flow.As<WeakListScreen>().Rows);
        Assert.Equal(weakId, row.WordId);
        Assert.Equal(LearningState.Mastered, row.State);
        Assert.NotNull(row.NextReviewDate);
    }

    [Fact]
    public void List_shows_rows_and_can_be_sorted_and_filtered_without_writing()
    {
        using var env = new FlowEnv();
        env.Time.Now = Day(1);
        var flow = env.Start();
        MakeWeak(flow, 2);
        var before = env.Dir.Snapshot();

        flow.OpenWeakList();
        var list = flow.As<WeakListScreen>();
        Assert.Equal(WeakListSort.WeakFirst, list.Sort);
        Assert.False(list.WeakOnly);
        Assert.Equal(2, list.Rows.Length);
        Assert.All(list.Rows, r => Assert.Equal(1, r.NormalMistakes));

        flow.ChangeWeakList(WeakListSort.RecentMistake, weakOnly: true);
        var changed = flow.As<WeakListScreen>();
        Assert.Equal(WeakListSort.RecentMistake, changed.Sort);
        Assert.True(changed.WeakOnly);
        Assert.Equal(2, changed.Rows.Length);

        flow.BackToHome();
        Assert.Equal(2, flow.As<HomeScreen>().Status.Weak);
        Assert.Equal(before, env.Dir.Snapshot());
    }

    [Fact]
    public void Weak_practice_with_nothing_to_practice_stays_on_list_and_writes_nothing()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.OpenWeakList();
        flow.ChangeWeakList(WeakListSort.RecentMistake, weakOnly: true);
        var before = env.Dir.Snapshot();

        flow.StartWeakPractice(Choices.Auto());

        var list = flow.As<WeakListScreen>();
        Assert.Equal(SelectionEmptyReason.NoWeakWords, list.EmptyReason);
        Assert.Equal(WeakListSort.RecentMistake, list.Sort);
        Assert.True(list.WeakOnly);
        Assert.Equal(before, env.Dir.Snapshot());

        flow.ChangeWeakList(WeakListSort.WeakFirst, weakOnly: false);
        Assert.Equal(SelectionEmptyReason.None, flow.As<WeakListScreen>().EmptyReason);
    }

    [Fact]
    public void Weak_practice_starts_for_due_review_even_without_weak_words()
    {
        using var env = new FlowEnv { WordsJson = Words.Json(new TestWord("apple")) };
        env.Time.Now = Day(1);
        var flow = env.Start();
        MakeWeak(flow, 1);
        CorrectWeakOnce(env, flow, Day(2));
        CorrectWeakOnce(env, flow, Day(3)); // 克服。再確認は 3日 + 7日
        env.Time.Now = Day(10);
        Assert.Equal(1, flow.As<HomeScreen>().Status.Mastered);

        flow.OpenWeakList();
        flow.StartWeakPractice(Choices.Manual(Japanese, 1));

        Assert.IsType<QuestionScreen>(flow.Screen);
    }

    [Fact]
    public void Weak_words_missing_from_word_data_cannot_be_practiced()
    {
        using var env = new FlowEnv { WordsJson = Words.Json(new TestWord("apple"), new TestWord("lemon")) };
        var first = env.Start();
        first.StartPractice(Choices.Manual(Japanese, 1));
        var weakId = first.As<QuestionScreen>().Question.WordId;
        first.AnswerAll(q => q.IsRetry);
        env.WordsJson = Words.Json(new TestWord(weakId == "apple" ? "lemon" : "apple"));
        var flow = env.Start();
        Assert.Equal(0, flow.As<HomeScreen>().Status.Weak); // 単語データに無い語は数えない
        flow.OpenWeakList();
        var row = Assert.Single(flow.As<WeakListScreen>().Rows); // 一覧には出る
        Assert.False(row.InWordSet);
        var before = env.Dir.Snapshot();

        flow.StartWeakPractice(Choices.Manual(Japanese, 1));

        Assert.NotEqual(SelectionEmptyReason.None, flow.As<WeakListScreen>().EmptyReason);
        Assert.Equal(before, env.Dir.Snapshot());
    }

    [Fact]
    public void Weak_practice_with_only_flash_available_is_empty()
    {
        using var env = new FlowEnv();
        MakeWeak(env.Start(), 2);
        env.Modes = [Flash];
        var flow = env.Start();
        flow.OpenWeakList();

        flow.StartWeakPractice(Choices.Auto());

        Assert.NotEqual(SelectionEmptyReason.None, flow.As<WeakListScreen>().EmptyReason);
    }

    [Fact]
    public void Weak_practice_forces_weak_only_even_if_choice_does_not()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        MakeWeak(flow, 1);
        flow.OpenWeakList();

        flow.StartWeakPractice(Choices.Manual(Japanese, 5)); // WeakWordsOnly = false

        Assert.True(env.Progress().Session!.Settings.Practice.WeakWordsOnly);
        Assert.Equal(1, flow.As<QuestionScreen>().Total); // 苦手の1語だけ
    }

    [Fact]
    public void Weak_practice_save_failure_stays_on_list_and_retry_starts()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        MakeWeak(flow, 1);
        flow.OpenWeakList();
        flow.ChangeWeakList(WeakListSort.RecentMistake, weakOnly: true);
        env.Files.FailWrites = true;

        flow.StartWeakPractice(Choices.Auto());

        var list = flow.As<WeakListScreen>();
        Assert.NotNull(list.Message);
        Assert.NotNull(list.Diagnostic);
        Assert.Equal(WeakListSort.RecentMistake, list.Sort);
        Assert.True(list.WeakOnly);
        Assert.Null(env.Progress().Session);

        env.Files.FailWrites = false;
        flow.StartWeakPractice(Choices.Auto());
        Assert.IsType<QuestionScreen>(flow.Screen);
    }

    [Fact]
    public void Practice_started_from_list_quits_to_resume_prompt_and_discard_goes_home()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        MakeWeak(flow, 1);
        flow.OpenWeakList();
        flow.StartWeakPractice(Choices.Auto());

        flow.Quit();
        flow.DiscardAndGoHome();

        Assert.Equal(1, flow.As<HomeScreen>().Status.Weak);
        Assert.IsType<ResumePromptScreen>(PracticeFlowAfterRestartWithSession(env));
    }

    [Fact]
    public void Home_status_is_filled_on_every_path_to_home()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        MakeWeak(flow, 1); // GoHome
        var expected = new StudyStatus(4, 0, 1, 0, 0);
        Assert.Equal(expected, flow.As<HomeScreen>().Status);

        flow.StartPractice(Choices.Auto(weakOnly: true, count: 5)); // 苦手1語の練習が始まる
        flow.Quit();
        flow.DiscardAndGoHome(); // Discard
        Assert.Equal(expected, flow.As<HomeScreen>().Status);

        env.Modes = [Flash];
        var noModes = env.Start(); // 起動(記録あり)
        Assert.Equal(expected, noModes.As<HomeScreen>().Status);
        noModes.StartPractice(Choices.Auto()); // 0語
        Assert.Equal(expected, noModes.As<HomeScreen>().Status);

        env.Modes = [Japanese, Audio, Blank, Flash];
        var failing = env.Start();
        env.Files.FailWrites = true;
        failing.StartPractice(Choices.Auto()); // 開始の保存失敗
        Assert.Equal(expected, failing.As<HomeScreen>().Status);
        env.Files.FailWrites = false;
    }

    [Fact]
    public void Home_status_after_start_fresh_shows_every_word_unseen()
    {
        using var env = new FlowEnv();
        env.Dir.Write(ProgressRepository.FileName, "{ broken");
        env.Dir.Write("progress.bak.json", "{ broken");
        var flow = env.Start();

        flow.StartFresh();

        Assert.Equal(new StudyStatus(5, 0, 0, 0, 0), flow.As<HomeScreen>().Status);
    }

    [Fact]
    public void Review_due_on_home_switches_at_japan_midnight()
    {
        using var env = new FlowEnv { WordsJson = Words.Json(new TestWord("apple")) };
        env.Time.Now = Day(1);
        var flow = env.Start();
        MakeWeak(flow, 1);
        CorrectWeakOnce(env, flow, Day(2));
        CorrectWeakOnce(env, flow, Day(3)); // 再確認は 11/10
        env.Time.Now = Day(9, 23, 59, 59);
        flow.OpenWeakList();
        flow.BackToHome();
        Assert.Equal(0, flow.As<HomeScreen>().Status.ReviewDue);

        env.Time.Now = Day(10, 0, 0, 0);
        flow.OpenWeakList();
        Assert.True(Assert.Single(flow.As<WeakListScreen>().Rows).ReviewDue);
        flow.BackToHome();

        Assert.Equal(1, flow.As<HomeScreen>().Status.ReviewDue);
    }

    [Fact]
    public void Invalid_arguments_throw_and_keep_the_list()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.OpenWeakList();
        var screen = flow.Screen;

        Assert.Throws<ArgumentNullException>(() => flow.StartWeakPractice(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => flow.ChangeWeakList((WeakListSort)99, false));

        Assert.Same(screen, flow.Screen);
    }

    private static FlowScreen PracticeFlowAfterRestartWithSession(FlowEnv env)
    {
        var flow = env.Start();
        flow.OpenWeakList();
        flow.StartWeakPractice(Choices.Auto());
        return env.Start().Screen; // 練習中に作り直す → 再開の選択
    }
}
