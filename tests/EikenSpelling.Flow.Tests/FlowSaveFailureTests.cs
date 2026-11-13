namespace EikenSpelling.Flow.Tests;

/// <summary>保存失敗時の約束(v1.0 13.3): 次へ進まない・回答を保持・再保存・二重記録しない。</summary>
public class FlowSaveFailureTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Answer_save_failure_blocks_next_until_retry_succeeds_and_records_once(int extraFailures)
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.StartPractice(Choices.Auto());
        var before = env.Dir.Snapshot();
        env.Files.FailWrites = true;

        var feedback = flow.AnswerCurrent(false);

        Assert.False(feedback.Saved);
        Assert.NotNull(feedback.SaveFailure);
        Assert.Throws<InvalidOperationException>(flow.Next);
        Assert.Throws<InvalidOperationException>(flow.Quit);
        for (var i = 0; i < extraFailures; i++)
        {
            flow.RetrySave();
            Assert.False(flow.As<FeedbackScreen>().Saved);
        }

        Assert.Equal(before, env.Dir.Snapshot());

        env.Files.FailWrites = false;
        flow.RetrySave();

        var saved = flow.As<FeedbackScreen>();
        Assert.True(saved.Saved);
        Assert.Null(saved.SaveFailure);
        Assert.Equal(feedback.IsCorrect, saved.IsCorrect);
        Assert.Equal(1, env.RecordedAnswers());
        Assert.Single(env.Progress().Progress.AppliedAnswerIds);
        flow.Next();
        Assert.Equal(2, flow.As<QuestionScreen>().Number);
    }

    [Fact]
    public void Retry_save_when_already_saved_is_rejected()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.StartPractice(Choices.Auto());
        flow.AnswerCurrent(true);

        Assert.Throws<InvalidOperationException>(flow.RetrySave);
    }

    [Fact]
    public void Restart_after_failed_save_resumes_from_last_saved_question()
    {
        using var env = new FlowEnv();
        var first = env.Start();
        first.StartPractice(Choices.Auto());
        first.AnswerCurrent(true);
        first.Next();
        var unsaved = first.As<QuestionScreen>().Question;
        env.Files.FailWrites = true;
        first.AnswerCurrent(true);
        env.Files.FailWrites = false;

        var flow = env.Start();
        flow.Resume();

        Assert.Equal(unsaved, flow.As<QuestionScreen>().Question);
        Assert.Equal(1, env.RecordedAnswers());
    }

    [Fact]
    public void Save_failure_on_last_question_retries_then_shows_result()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.StartPractice(Choices.Auto(count: 1));
        env.Files.FailWrites = true;

        var feedback = flow.AnswerCurrent(true);

        Assert.True(feedback.Completed);
        Assert.False(feedback.Saved);
        Assert.Throws<InvalidOperationException>(flow.Next);
        env.Files.FailWrites = false;
        flow.RetrySave();
        flow.Next();
        Assert.IsType<ResultScreen>(flow.Screen);
        Assert.Null(env.Progress().Session);
    }

    [Fact]
    public void Restart_after_failed_save_on_last_question_resumes_at_last_question()
    {
        using var env = new FlowEnv();
        var first = env.Start();
        first.StartPractice(Choices.Auto(count: 1));
        env.Files.FailWrites = true;
        first.AnswerCurrent(true);
        env.Files.FailWrites = false;

        var flow = env.Start();

        Assert.Equal(0, flow.As<ResumePromptScreen>().Session.Answered);
        Assert.Equal(0, env.RecordedAnswers());
    }

    [Fact]
    public void Save_failure_on_last_retry_question_retries_then_shows_result()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.StartPractice(Choices.Auto(count: 1));
        flow.AnswerCurrent(false);
        flow.Next();
        env.Files.FailWrites = true;

        Assert.True(flow.AnswerCurrent(true).Completed);

        env.Files.FailWrites = false;
        flow.RetrySave();
        flow.Next();
        Assert.Single(flow.As<ResultScreen>().Summary.Retries);
        Assert.Equal(2, env.RecordedAnswers());
    }

    [Fact]
    public void Start_save_failure_stays_home_with_message_and_next_start_works()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        env.Files.FailWrites = true;

        flow.StartPractice(Choices.Auto());

        Assert.NotNull(flow.As<HomeScreen>().Message);
        Assert.Null(env.ProgressJsonText);
        env.Files.FailWrites = false;
        flow.StartPractice(Choices.Auto());
        Assert.IsType<QuestionScreen>(flow.Screen);
        Assert.Empty(env.Progress().Session!.Answers);
    }

    [Fact]
    public void Discard_save_failure_stays_on_prompt_and_resume_is_still_possible()
    {
        using var env = new FlowEnv();
        var first = env.Start();
        first.StartPractice(Choices.Auto());
        first.AnswerCurrent(true);
        var flow = env.Start();
        env.Files.FailWrites = true;

        flow.DiscardAndGoHome();

        var prompt = flow.As<ResumePromptScreen>();
        Assert.NotNull(prompt.Message);
        Assert.True(prompt.CanResume);
        Assert.NotNull(env.Progress().Session);

        env.Files.FailWrites = false;
        flow.DiscardAndGoHome();
        Assert.IsType<HomeScreen>(flow.Screen);
        Assert.Null(env.Progress().Session);
        Assert.Equal(1, env.RecordedAnswers());
    }

    [Fact]
    public void Discard_failure_then_resume_continues_the_session()
    {
        using var env = new FlowEnv();
        var first = env.Start();
        first.StartPractice(Choices.Auto());
        first.AnswerCurrent(true);
        var flow = env.Start();
        env.Files.FailWrites = true;
        flow.DiscardAndGoHome();
        env.Files.FailWrites = false;

        flow.Resume();

        Assert.Equal(2, flow.As<QuestionScreen>().Number);
    }
}
