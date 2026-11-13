using EikenSpelling.Core.Domain;
using EikenSpelling.Persistence;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Flow.Tests;

public class FlowResumeTests
{
    [Fact]
    public void Restart_mid_session_offers_resume_and_continues_without_duplicates()
    {
        using var env = new FlowEnv();
        var first = env.Start();
        first.StartPractice(Choices.Auto(count: 5));
        first.AnswerCurrent(true);
        first.Next();
        first.AnswerCurrent(false);
        first.Next();
        var current = first.As<QuestionScreen>().Question;

        var flow = env.Start();

        var prompt = flow.As<ResumePromptScreen>();
        Assert.True(prompt.CanResume);
        Assert.Equal(2, prompt.Session.Answered);
        Assert.Equal(5, prompt.Session.Total);
        Assert.Equal(FlowEnv.T0, prompt.Session.StartedAt);
        flow.Resume();
        var q = flow.As<QuestionScreen>();
        Assert.Equal(current, q.Question);
        Assert.Equal(3, q.Number);

        var result = flow.AnswerAll(_ => true);

        Assert.Equal(4, result.Summary.NormalCorrect);
        Assert.Equal(5 + 1, env.RecordedAnswers()); // 通常 5 + 再練習 1、二重にならない
    }

    [Fact]
    public void Discard_keeps_records_and_removes_only_the_session()
    {
        using var env = new FlowEnv();
        var first = env.Start();
        first.StartPractice(Choices.Auto());
        first.AnswerCurrent(false);

        var flow = env.Start();
        flow.DiscardAndGoHome();

        Assert.IsType<HomeScreen>(flow.Screen);
        var saved = env.Progress();
        Assert.Null(saved.Session);
        Assert.Empty(saved.Progress.AppliedAnswerIds);
        Assert.Equal(1, env.RecordedAnswers());
        flow.StartPractice(Choices.Auto());
        Assert.IsType<QuestionScreen>(flow.Screen);
    }

    [Fact]
    public void Quit_returns_to_resume_prompt_without_writing()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.StartPractice(Choices.Auto());
        flow.AnswerCurrent(true);
        flow.Next();
        var current = flow.As<QuestionScreen>().Question;
        var before = env.Dir.Snapshot();

        flow.Quit();

        var prompt = flow.As<ResumePromptScreen>();
        Assert.True(prompt.CanResume);
        Assert.Equal(1, prompt.Session.Answered);
        Assert.Equal(before, env.Dir.Snapshot());
        Assert.Throws<InvalidOperationException>(() => flow.StartPractice(Choices.Auto()));
        flow.Resume();
        Assert.Equal(current, flow.As<QuestionScreen>().Question);
    }

    [Fact]
    public void Quit_then_discard_then_start_leaves_no_old_answer_ids()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.StartPractice(Choices.Auto());
        flow.AnswerCurrent(true);
        flow.Next();
        var oldId = env.Progress().Session!.SessionId;

        flow.Quit();
        flow.DiscardAndGoHome();
        flow.StartPractice(Choices.Auto());
        flow.AnswerCurrent(true);

        Assert.All(env.Progress().Progress.AppliedAnswerIds, id => Assert.NotEqual(oldId, id.SessionId));
    }

    [Fact]
    public void Resume_in_first_retry_gives_same_result_as_uninterrupted_run()
    {
        var expected = Run(interruptAt: null);
        var actual = Run(interruptAt: 6); // 通常 5 問の後の再練習 1 問目で再起動

        Assert.Equal(expected.Summary.NormalCorrect, actual.Summary.NormalCorrect);
        Assert.Equal(expected.Summary.MistakenWords.ToArray(), actual.Summary.MistakenWords.ToArray());
        Assert.Equal(expected.Summary.Retries.ToArray(), actual.Summary.Retries.ToArray());
        Assert.Equal(expected.Summary.StateChanges.ToArray(), actual.Summary.StateChanges.ToArray());
    }

    [Fact]
    public void Resume_in_second_retry_appended_at_end_continues_with_same_question()
    {
        using var env = new FlowEnv();
        env.Dir.Write(SettingsRepository.FileName, """{ "settingsVersion": 1, "practice": { "retryCount": 2 } }""");
        var first = env.Start();
        first.StartPractice(Choices.Auto(count: 1));
        first.AnswerCurrent(false);
        first.Next();
        first.AnswerCurrent(false); // 再練習1回目も誤答 → 2回目が列の最後に足される
        first.Next();
        var retry2 = first.As<QuestionScreen>();
        Assert.True(retry2.Question.IsRetry);

        var flow = env.Start();
        flow.Resume();

        var q = flow.As<QuestionScreen>();
        Assert.Equal(retry2.Question, q.Question);
        Assert.Equal(retry2.Number, q.Number);
        Assert.Equal(retry2.Total, q.Total);
        flow.AnswerCurrent(true);
        flow.Next();
        Assert.Equal(2, flow.As<ResultScreen>().Summary.Retries.Single().Attempts);
    }

    [Fact]
    public void Resume_keeps_flash_seconds_from_session_even_if_settings_changed()
    {
        using var env = new FlowEnv();
        var first = env.Start();
        first.StartPractice(Choices.Auto(flash: 0.5));
        env.Dir.Write(SettingsRepository.FileName, """{ "settingsVersion": 1, "practice": { "flashSeconds": 2.0 } }""");

        var flow = env.Start();
        flow.Resume();

        Assert.Equal(0.5, flow.As<QuestionScreen>().FlashSeconds);
    }

    [Fact]
    public void Resume_across_midnight_records_answers_on_the_actual_day()
    {
        using var env = new FlowEnv();
        env.Time.Now = new DateTimeOffset(2026, 11, 1, 23, 59, 59, TimeSpan.FromHours(9));
        var first = env.Start();
        first.StartPractice(Choices.Auto(count: 2));
        first.AnswerCurrent(true);

        env.Time.Now = env.Time.Now.AddSeconds(1);
        var flow = env.Start();
        flow.Resume();
        var wordId = flow.As<QuestionScreen>().Question.WordId;
        flow.AnswerCurrent(true);

        var record = env.Progress().Progress.Word(wordId)!;
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.FromHours(9)), record.LastNormalAskedAt);
    }

    // ---- 再開できない ----

    [Fact]
    public void Removed_word_prevents_resume_but_allows_discard()
    {
        using var env = new FlowEnv();
        var first = env.Start();
        first.StartPractice(Choices.Auto(count: 5));
        first.AnswerCurrent(true);
        env.WordsJson = Words.Json(Words.Five.Take(1).ToArray()); // 残りの単語が消えた(順番によらず4語は消える)

        var flow = env.Start();

        var prompt = flow.As<ResumePromptScreen>();
        Assert.False(prompt.CanResume);
        Assert.NotNull(prompt.CannotResumeReason);
        Assert.Throws<InvalidOperationException>(flow.Resume);
        flow.DiscardAndGoHome();
        Assert.IsType<HomeScreen>(flow.Screen);
        Assert.Equal(1, env.RecordedAnswers());
    }

    [Fact]
    public void Audio_no_longer_available_prevents_resume()
    {
        using var env = new FlowEnv();
        var first = env.Start();
        first.StartPractice(Choices.Manual(Audio));
        env.Modes = [Japanese, Blank, Flash];

        Assert.False(env.Start().As<ResumePromptScreen>().CanResume);
    }

    [Fact]
    public void Japanese_no_longer_eligible_prevents_resume()
    {
        using var env = new FlowEnv();
        env.Start().StartPractice(Choices.Manual(Japanese));
        env.WordsJson = Words.Json(Words.Five.Select(w => w with { Japanese = false }).ToArray());

        Assert.False(env.Start().As<ResumePromptScreen>().CanResume);
    }

    [Fact]
    public void Blank_without_blanks_prevents_resume()
    {
        using var env = new FlowEnv { WordsJson = Words.Json(new TestWord("apple", Blanks: "ap__e")) };
        env.Start().StartPractice(Choices.Manual(Blank, count: 1));
        env.WordsJson = Words.Json(new TestWord("apple"));

        Assert.False(env.Start().As<ResumePromptScreen>().CanResume);
    }

    [Fact]
    public void Answered_questions_whose_words_were_removed_do_not_block_resume()
    {
        using var env = new FlowEnv();
        var first = env.Start();
        first.StartPractice(Choices.Auto(count: 2));
        var answered = first.As<QuestionScreen>().Question.WordId;
        first.AnswerCurrent(true);
        env.WordsJson = Words.Json(Words.Five.Where(w => w.Id != answered).ToArray());

        Assert.True(env.Start().As<ResumePromptScreen>().CanResume);
    }

    [Fact]
    public void Mistaken_word_removed_before_its_retry_is_added_prevents_resume()
    {
        using var env = new FlowEnv();
        var first = env.Start();
        first.StartPractice(Choices.Auto(count: 5));
        var mistaken = first.As<QuestionScreen>().Question.WordId;
        first.AnswerCurrent(false); // 再練習は通常問題を終えてから列に足される(まだ列に無い)
        env.WordsJson = Words.Json(Words.Five.Where(w => w.Id != mistaken).ToArray());

        var prompt = env.Start().As<ResumePromptScreen>();

        Assert.False(prompt.CanResume);
    }

    [Fact]
    public void Retry_mode_not_available_for_mistaken_word_prevents_resume()
    {
        using var env = new FlowEnv();
        var first = env.Start();
        first.StartPractice(Choices.Auto(count: 5));
        var mistaken = first.As<QuestionScreen>().Question;
        first.AnswerCurrent(false);
        var saved = env.Progress().Session!.Questions[0];
        var retryMode = RetryModeRule.Choose(saved.Mode, saved.EligibleModes, autoMix: true);
        Assert.NotEqual(Flash, retryMode); // この単語データでは日本語か音声になる
        env.WordsJson = Words.Json(Words.Five
            .Select(w => w.Id != mistaken.WordId ? w : retryMode == Japanese ? w with { Japanese = false } : w with { Audio = false })
            .ToArray());

        Assert.False(env.Start().As<ResumePromptScreen>().CanResume);
    }

    [Fact]
    public void Retry_mode_not_available_for_unanswered_question_prevents_resume()
    {
        using var env = new FlowEnv();
        var first = env.Start();
        first.StartPractice(Choices.Auto(count: 5));
        first.AnswerCurrent(true);
        // まだ答えていない通常問題のうち、誤答したときの再練習のモードが自分のモードと違うもの
        var target = env.Progress().Session!.Questions.Skip(1)
            .Select(q => (Question: q, Retry: RetryModeRule.Choose(q.Mode, q.EligibleModes, autoMix: true)))
            .First(x => x.Retry != x.Question.Mode && x.Retry is Japanese or Audio);
        env.WordsJson = Words.Json(Words.Five
            .Select(w => w.Id != target.Question.WordId ? w
                : target.Retry == Japanese ? w with { Japanese = false } : w with { Audio = false })
            .ToArray());

        var prompt = env.Start().As<ResumePromptScreen>();

        Assert.False(prompt.CanResume); // 問題そのものは出せるが、誤答したら答えようのない再練習になる
    }

    [Fact]
    public void Retry_count_zero_does_not_check_retry_modes()
    {
        using var env = new FlowEnv();
        env.Dir.Write(SettingsRepository.FileName, """{ "settingsVersion": 1, "practice": { "retryCount": 0 } }""");
        var first = env.Start();
        first.StartPractice(Choices.Manual(Audio));
        first.AnswerCurrent(false);
        env.WordsJson = Words.Json(Words.Five.Select(w => w with { Japanese = false }).ToArray());

        Assert.True(env.Start().As<ResumePromptScreen>().CanResume); // 手動は元のモードのまま。再練習も無い
    }

    [Fact]
    public void Mistaken_word_still_available_for_retry_allows_resume()
    {
        using var env = new FlowEnv();
        var first = env.Start();
        first.StartPractice(Choices.Auto(count: 5));
        first.AnswerCurrent(false);

        Assert.True(env.Start().As<ResumePromptScreen>().CanResume);
    }

    [Fact]
    public void Changed_spelling_for_same_id_prevents_resume()
    {
        using var env = new FlowEnv();
        env.Start().StartPractice(Choices.Auto(count: 5));
        env.WordsJson = Words.Json(Words.Five).Replace("\"word\":\"apple\"", "\"word\":\"appel\"", StringComparison.Ordinal);
        Assert.Contains("appel", env.WordsJson, StringComparison.Ordinal);

        Assert.False(env.Start().As<ResumePromptScreen>().CanResume);
    }

    private static ResultScreen Run(int? interruptAt)
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.StartPractice(Choices.Auto(count: 5));
        var n = 0;
        while (flow.Screen is QuestionScreen q)
        {
            if (++n == interruptAt)
            {
                flow = env.Start();
                flow.Resume();
                q = flow.As<QuestionScreen>();
            }

            flow.AnswerCurrent(q.Number % 2 == 0 && !q.Question.IsRetry); // 奇数番目は誤答 → 再練習あり
            flow.Next();
        }

        return flow.As<ResultScreen>();
    }
}
