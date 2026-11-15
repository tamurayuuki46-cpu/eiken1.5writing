using EikenSpelling.Core.Models;
using EikenSpelling.Persistence;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Flow.Tests;

/// <summary>ホームの「おすすめ学習」と4モードの練習(UI v0.3 S-02・S-03、AC-R01・R03)。</summary>
public class FlowFourModesTests
{
    /// <summary>4モードすべてで出せる単語(穴あきあり・音声可・日本語可)。</summary>
    private static readonly TestWord[] AllModeWords =
    [
        new("apple", "りんご", "ap_le"), new("banana", "バナナ", "ban_na"), new("cherry", "さくらんぼ", "ch_rry"),
        new("grape", "ぶどう", "gr_pe"), new("lemon", "レモン", "le_on"),
    ];

    private static FlowEnv Env(HashSet<LearningMode>? modes = null) =>
        new() { WordsJson = Words.Json(AllModeWords), Modes = modes ?? [Japanese, Audio, Blank, Flash] };

    // ---- おすすめ学習 ----

    [Fact]
    public void Recommended_uses_saved_count_with_auto_mix_and_without_weak_only_and_writes_no_settings()
    {
        using var env = Env();
        var settings = """{ "settingsVersion": 1, "practice": { "questionCount": 3, "autoMix": false, "manualMode": "Blank", "weakWordsOnly": true } }""";
        env.Dir.Write(SettingsRepository.FileName, settings);
        var flow = env.Start();

        flow.StartRecommended();

        var session = env.Progress().Session!;
        Assert.Equal(3, session.NormalCount);
        Assert.True(session.Settings.Practice.AutoMix);
        Assert.False(session.Settings.Practice.WeakWordsOnly);
        Assert.Equal(settings, env.Dir.Read(SettingsRepository.FileName)); // 基本設定は書き換えない
        flow.AnswerAll(_ => true);
        flow.GoHome();
        var defaults = flow.As<HomeScreen>().Defaults; // 保存済みの基本設定はそのまま
        Assert.Equal((3, false, Blank, true), (defaults.QuestionCount, defaults.AutoMix, defaults.ManualMode, defaults.WeakWordsOnly));
    }

    [Fact]
    public void Recommended_reaches_result()
    {
        using var env = Env();
        var flow = env.Start();

        flow.StartRecommended();
        var result = flow.AnswerAll(_ => true);

        Assert.Equal(5, result.Summary.NormalCount); // 既定の10問だが、出せる単語は5語
        Assert.Equal(5, result.Summary.NormalCorrect);
        Assert.Null(env.Progress().Session);
    }

    [Fact]
    public void Recommended_is_only_accepted_on_home()
    {
        using var env = Env();
        var flow = env.Start();
        flow.StartRecommended();

        Assert.Throws<InvalidOperationException>(flow.StartRecommended);
    }

    // ---- 自由選択: モードを個別に指定して結果まで ----

    [Theory]
    [InlineData(Japanese)]
    [InlineData(Audio)]
    [InlineData(Blank)]
    [InlineData(Flash)]
    public void Each_manual_mode_runs_to_result(LearningMode mode)
    {
        using var env = Env();
        var flow = env.Start();

        flow.StartPractice(Choices.Manual(mode, count: 3));

        var seen = new List<LearningMode>();
        while (flow.Screen is QuestionScreen q)
        {
            seen.Add(q.Question.Mode);
            flow.AnswerCurrent(true);
            flow.Next();
        }

        Assert.IsType<ResultScreen>(flow.Screen);
        Assert.Equal(3, seen.Count);
        Assert.All(seen, m => Assert.Equal(mode, m));
        Assert.Equal(3, env.RecordedAnswers());
    }

    [Fact]
    public void Manual_audio_on_device_without_audio_gives_reason_and_writes_nothing()
    {
        using var env = Env([Japanese, Blank, Flash]);
        var flow = env.Start();

        flow.StartPractice(Choices.Manual(Audio));

        Assert.Equal(Core.Application.SelectionEmptyReason.ManualModeUnavailable, flow.As<HomeScreen>().EmptyReason);
        Assert.Null(env.ProgressJsonText);
    }

    // ---- 自動ミックスは使えて適格なモードだけ ----

    [Fact]
    public void Auto_mix_without_device_audio_never_contains_audio()
    {
        using var env = Env([Japanese, Blank, Flash]);
        var flow = env.Start();

        flow.StartPractice(Choices.Auto(count: 5));

        Assert.DoesNotContain(env.Progress().Session!.Questions, q => q.Mode == Audio || q.EligibleModes.Contains(Audio));
    }

    [Fact]
    public void Auto_mix_never_gives_a_word_a_mode_it_is_not_eligible_for()
    {
        using var env = new FlowEnv
        {
            WordsJson = Words.Json(
                new TestWord("apple", Blanks: "ap_le", Audio: false),
                new TestWord("banana", Blanks: null, Audio: true),
                new TestWord("cherry", Blanks: "ch_rry", Audio: false),
                new TestWord("grape", Blanks: null, Audio: true),
                new TestWord("lemon", Blanks: "le_on", Audio: false)),
        };
        var flow = env.Start();

        flow.StartPractice(Choices.Auto(count: 5));

        var questions = env.Progress().Session!.Questions;
        Assert.DoesNotContain(questions, q => q.Mode == Audio && q.WordId is "apple" or "cherry" or "lemon");
        Assert.DoesNotContain(questions, q => q.Mode == Blank && q.WordId is "banana" or "grape");
        Assert.All(questions, q => Assert.Contains(q.Mode, q.EligibleModes));
    }

    // ---- フラッシュ ----

    private static PracticeFlow StartFlash(FlowEnv env, int count = 2)
    {
        var flow = env.Start();
        flow.StartPractice(Choices.Manual(Flash, count) with { FlashSeconds = 2.0 });
        return flow;
    }

    [Fact]
    public void Flash_question_must_be_shown_then_hidden_before_answering()
    {
        using var env = Env();
        var flow = StartFlash(env);
        var q = flow.As<QuestionScreen>();
        Assert.Equal(FlashPhase.NotStarted, q.Flash);
        Assert.Equal(2.0, q.FlashSeconds);
        Assert.False(q.CanAnswer);
        Assert.False(q.CanSkip);
        Assert.Throws<InvalidOperationException>(() => flow.Answer(q.Question.Word));
        Assert.Throws<InvalidOperationException>(flow.EndFlash);

        flow.ShowFlash();

        Assert.Equal(FlashPhase.Showing, flow.As<QuestionScreen>().Flash);
        Assert.Equal(0, env.Progress().Session!.FlashShownStep); // 見せる前に印を保存した
        Assert.Throws<InvalidOperationException>(() => flow.Answer(q.Question.Word));
        Assert.Throws<InvalidOperationException>(flow.Skip);
        Assert.Throws<InvalidOperationException>(flow.ShowFlash);

        flow.EndFlash();

        var hidden = flow.As<QuestionScreen>();
        Assert.Equal(FlashPhase.Hidden, hidden.Flash);
        Assert.True(hidden.CanAnswer);
        Assert.True(hidden.CanSkip);
        flow.Answer(q.Question.Word);
        flow.Next();
        Assert.Equal(FlashPhase.NotStarted, flow.As<QuestionScreen>().Flash); // 次の問題はまだ見せていない
    }

    [Fact]
    public void Quit_before_showing_resumes_with_word_shown_once()
    {
        using var env = Env();
        var flow = StartFlash(env);
        var word = flow.As<QuestionScreen>().Question.WordId;
        flow.Quit();

        var restarted = env.Start();
        restarted.Resume();

        var q = restarted.As<QuestionScreen>();
        Assert.Equal(word, q.Question.WordId);
        Assert.Equal(FlashPhase.NotStarted, q.Flash); // 再開時に1回見せる
    }

    [Fact]
    public void Quit_while_showing_resumes_at_input_without_showing_word_again()
    {
        using var env = Env();
        var flow = StartFlash(env);
        var word = flow.As<QuestionScreen>().Question.WordId;
        flow.ShowFlash();
        flow.Quit(); // 表示の途中で中断

        // 同じ起動の中で再開しても、再起動しても、入力欄から(単語を再表示しない)
        flow.Resume();
        Assert.Equal((word, FlashPhase.Hidden), (flow.As<QuestionScreen>().Question.WordId, flow.As<QuestionScreen>().Flash));

        var restarted = env.Start();
        restarted.Resume();
        var q = restarted.As<QuestionScreen>();
        Assert.Equal(word, q.Question.WordId);
        Assert.Equal(FlashPhase.Hidden, q.Flash);
        Assert.True(q.CanAnswer);
        Assert.Throws<InvalidOperationException>(restarted.ShowFlash);
        Assert.Equal(0, env.RecordedAnswers()); // 中断・再開だけでは採点しない
    }

    [Fact]
    public void Crash_during_showing_restarts_at_input()
    {
        using var env = Env();
        var flow = StartFlash(env);
        flow.ShowFlash(); // ここで強制終了したとみなす(EndFlash も Quit も呼ばない)

        var restarted = env.Start();
        restarted.Resume();

        Assert.Equal(FlashPhase.Hidden, restarted.As<QuestionScreen>().Flash);
    }

    [Fact]
    public void Flash_start_save_failure_does_not_show_word_and_retry_save_uses_same_mark()
    {
        using var env = Env();
        var flow = StartFlash(env);
        var before = env.Dir.Snapshot();
        env.Files.FailWrites = true;

        flow.ShowFlash();

        var failed = flow.As<QuestionScreen>();
        Assert.Equal(FlashPhase.NotStarted, failed.Flash); // 保存できるまで見せない
        Assert.NotNull(failed.SaveFailure);
        Assert.NotNull(failed.Diagnostic);
        Assert.Throws<InvalidOperationException>(flow.ShowFlash);
        Assert.Throws<InvalidOperationException>(flow.Quit);
        Assert.Throws<InvalidOperationException>(flow.Skip);
        Assert.Equal(before, env.Dir.Snapshot());

        env.Files.FailWrites = false;
        flow.RetrySave();

        Assert.Equal(FlashPhase.Showing, flow.As<QuestionScreen>().Flash);
        Assert.Null(flow.As<QuestionScreen>().SaveFailure);
        Assert.Equal(0, env.Progress().Session!.FlashShownStep);
    }

    [Fact]
    public void Flash_question_can_be_skipped_after_hiding_and_deferred_shows_word_again()
    {
        using var env = Env();
        var flow = StartFlash(env);
        var first = flow.As<QuestionScreen>().Question.WordId;
        flow.ShowFlash();
        flow.EndFlash();
        flow.Skip();
        flow.AnswerCurrent(true);
        flow.Next();

        var deferred = flow.As<QuestionScreen>();
        Assert.Equal(first, deferred.Question.WordId);
        Assert.Equal(Core.Application.QuestionPhase.Deferred, deferred.Phase);
        Assert.Equal(FlashPhase.NotStarted, deferred.Flash); // 後回しで出たときはもう一度見せる
    }

    [Fact]
    public void Non_flash_question_has_no_flash_phase_and_rejects_show()
    {
        using var env = Env();
        var flow = env.Start();
        flow.StartPractice(Choices.Manual(Japanese, count: 1));

        Assert.Equal(FlashPhase.None, flow.As<QuestionScreen>().Flash);
        Assert.True(flow.As<QuestionScreen>().CanAnswer);
        Assert.Throws<InvalidOperationException>(flow.ShowFlash);
    }
}
