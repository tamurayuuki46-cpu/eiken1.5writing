using EikenSpelling.Core.Application;
using EikenSpelling.Persistence;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Flow.Tests;

public class FlowPracticeTests
{
    [Fact]
    public void Full_run_saves_after_every_answer_and_shows_result()
    {
        using var env = new FlowEnv();
        var flow = env.Start();

        flow.StartPractice(Choices.Auto(count: 5));
        Assert.Empty(env.Progress().Session!.Answers); // 開始の時点で保存
        for (var i = 1; i <= 5; i++)
        {
            var q = flow.As<QuestionScreen>();
            Assert.Equal(i, q.Number);
            Assert.Equal(5, q.Total);
            var feedback = flow.AnswerCurrent(true);
            Assert.True(feedback.Saved);
            Assert.Equal(i, env.RecordedAnswers());
            Assert.Equal(i == 5, feedback.Completed);
            flow.Next();
        }

        var result = flow.As<ResultScreen>();
        Assert.Equal(5, result.Summary.NormalCorrect);
        Assert.Null(env.Progress().Session); // 完了したセッションは保存しない
        flow.GoHome();
        Assert.IsType<HomeScreen>(flow.Screen);
    }

    [Fact]
    public void Wrong_answer_shows_diff_and_correct_word()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.StartPractice(Choices.Auto());
        var word = flow.As<QuestionScreen>().Question.Word;

        flow.Answer(word + "x");

        var feedback = flow.As<FeedbackScreen>();
        Assert.False(feedback.IsCorrect);
        Assert.Equal(word, feedback.CorrectWord);
        Assert.NotEmpty(feedback.Diff);
    }

    [Fact]
    public void Correct_answer_has_no_diff()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.StartPractice(Choices.Auto());

        Assert.Empty(flow.AnswerCurrent(true).Diff);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_input_is_a_wrong_answer(string input)
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.StartPractice(Choices.Auto());

        flow.Answer(input);

        var feedback = flow.As<FeedbackScreen>();
        Assert.False(feedback.IsCorrect);
        Assert.NotEmpty(feedback.Diff);
    }

    [Fact]
    public void Null_input_throws_and_keeps_the_question()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.StartPractice(Choices.Auto());
        var screen = flow.Screen;

        Assert.Throws<ArgumentNullException>(() => flow.Answer(null!));

        Assert.Same(screen, flow.Screen);
        Assert.Equal(0, env.RecordedAnswers());
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    public void Long_wrong_input_is_recorded_up_to_the_limit(int length, int recorded)
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.StartPractice(Choices.Auto());
        var wordId = flow.As<QuestionScreen>().Question.WordId;

        flow.Answer(new string('z', length));

        var record = env.Progress().Progress.Word(wordId)!.Modes.Values.Single();
        Assert.Equal(recorded, record.LastWrongInput!.Length);
    }

    [Fact]
    public void Retry_question_comes_after_normal_questions()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.StartPractice(Choices.Auto(count: 2));
        var first = flow.As<QuestionScreen>().Question.WordId;

        flow.AnswerCurrent(false);
        flow.Next();
        flow.AnswerCurrent(true);
        flow.Next();

        var retry = flow.As<QuestionScreen>();
        Assert.True(retry.Question.IsRetry);
        Assert.Equal(first, retry.Question.WordId);
        Assert.Equal(3, retry.Number);
        Assert.Equal(3, retry.Total);
        flow.AnswerCurrent(true);
        flow.Next();
        var summary = flow.As<ResultScreen>().Summary;
        Assert.Equal([new RetryResult(first, first, 1, true)], summary.Retries.ToArray());
    }

    [Fact]
    public void Question_shows_japanese_and_flash_seconds_of_the_choice()
    {
        using var env = new FlowEnv { WordsJson = Words.Json(new TestWord("apple", Ja: "りんご")) };
        var flow = env.Start();

        flow.StartPractice(new PracticeChoice(false, Japanese, 5, false, 0.5));

        var q = flow.As<QuestionScreen>();
        Assert.Equal("りんご", q.Question.Japanese);
        Assert.Equal(Japanese, q.Question.Mode);
        Assert.Equal(0.5, q.FlashSeconds);
    }

    [Fact]
    public void Blank_question_shows_blanks_from_word_data()
    {
        using var env = new FlowEnv { WordsJson = Words.Json(new TestWord("apple", Blanks: "ap__e")) };
        var flow = env.Start();

        flow.StartPractice(Choices.Manual(Blank, count: 1));

        Assert.Equal("ap__e", flow.As<QuestionScreen>().Question.Blanks);
    }

    [Fact]
    public void Choice_is_kept_in_the_session_and_settings_file_is_not_written()
    {
        using var env = new FlowEnv();
        var flow = env.Start();

        flow.StartPractice(new PracticeChoice(false, Audio, 5, false, 2.0));

        var practice = env.Progress().Session!.Settings.Practice;
        Assert.False(practice.AutoMix);
        Assert.Equal(Audio, practice.ManualMode);
        Assert.Equal(2.0, practice.FlashSeconds);
        Assert.Null(env.Dir.Read(SettingsRepository.FileName));
    }

    [Fact]
    public void Invalid_choice_throws_and_changes_nothing()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        var screen = flow.Screen;

        Assert.ThrowsAny<ArgumentException>(() => flow.StartPractice(Choices.Auto(count: 0)));

        Assert.Same(screen, flow.Screen);
        Assert.Null(env.ProgressJsonText);
    }

    // ---- 0語 ----

    [Fact]
    public void Weak_only_without_weak_words_stays_home_with_reason()
    {
        using var env = new FlowEnv();
        var flow = env.Start();

        flow.StartPractice(Choices.Auto(weakOnly: true));

        Assert.Equal(SelectionEmptyReason.NoWeakWords, flow.As<HomeScreen>().EmptyReason);
        Assert.Null(env.ProgressJsonText);
    }

    [Fact]
    public void Manual_mode_not_available_stays_home_with_reason()
    {
        using var env = new FlowEnv { Modes = [Japanese, Blank, Flash] };
        var flow = env.Start();

        flow.StartPractice(Choices.Manual(Audio));

        Assert.Equal(SelectionEmptyReason.ManualModeUnavailable, flow.As<HomeScreen>().EmptyReason);
        Assert.Null(env.ProgressJsonText);
    }

    [Fact]
    public void Vocabulary_excluding_every_word_stays_home_with_reason()
    {
        using var env = new FlowEnv();
        env.Dir.Write(SettingsRepository.FileName, """{ "settingsVersion": 1, "vocabulary": { "enabledSources": ["NAWL"] } }""");
        var flow = env.Start();

        flow.StartPractice(Choices.Auto());

        Assert.Equal(SelectionEmptyReason.NoWordsInSources, flow.As<HomeScreen>().EmptyReason);
        Assert.Null(env.ProgressJsonText);
    }

    [Fact]
    public void Words_without_mastery_modes_stay_home_with_reason()
    {
        using var env = new FlowEnv { WordsJson = Words.Json(new TestWord("apple", Audio: false, Japanese: false)) };
        var flow = env.Start();

        flow.StartPractice(Choices.Auto());

        Assert.Equal(SelectionEmptyReason.NoEligibleWords, flow.As<HomeScreen>().EmptyReason);
        Assert.Null(env.ProgressJsonText);
    }

    [Fact]
    public void Empty_reason_is_cleared_by_the_next_successful_start()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.StartPractice(Choices.Auto(weakOnly: true));

        flow.StartPractice(Choices.Auto());

        Assert.IsType<QuestionScreen>(flow.Screen);
    }

    // ---- 日付をまたぐ ----

    [Fact]
    public void Mastery_counts_days_in_japan_time_across_midnight_and_summary_agrees()
    {
        using var env = new FlowEnv { WordsJson = Words.Json(new TestWord("apple", Ja: "りんご")) };
        var flow = env.Start();
        flow.StartPractice(Choices.Manual(Japanese, count: 1));
        flow.AnswerCurrent(false); // 苦手に
        flow.Next();
        flow.AnswerAll(_ => true); // 再練習

        // 翌日 23:59:59(日本時間)に1回目、その1秒後(翌々日 0:00:00)に2回目
        env.Time.Now = new DateTimeOffset(2026, 11, 2, 23, 59, 59, TimeSpan.FromHours(9));
        flow.GoHome();
        flow.StartPractice(new PracticeChoice(false, Japanese, 1, true, 1.0));
        Assert.False(flow.AnswerCurrent(true).Change.BecameMastered);
        flow.Next();
        flow.GoHome();

        env.Time.Now = env.Time.Now.AddSeconds(1);
        flow.StartPractice(new PracticeChoice(false, Japanese, 1, true, 1.0));
        var feedback = flow.AnswerCurrent(true);
        flow.Next();

        Assert.True(feedback.Change.BecameMastered);
        Assert.Equal(
            [new StateChange("apple", "apple", StateChangeKind.Mastered)],
            flow.As<ResultScreen>().Summary.StateChanges.ToArray());
    }
}
