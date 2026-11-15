using EikenSpelling.Flow;
using EikenSpelling.Flow.Tests;
using EikenSpelling.Persistence;
using EikenSpelling.Presentation.Screens;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Presentation.Tests;

/// <summary>ホームの2つの入口と、音声・フラッシュの出題画面(UI v0.3 S-02・S-03)。本物の Flow で確かめる。</summary>
public class FourModesViewModelTests
{
    private static readonly TestWord[] AllModeWords =
    [
        new("apple", "りんご", "ap_le"), new("banana", "バナナ", "ban_na"), new("cherry", "さくらんぼ", "ch_rry"),
        new("grape", "ぶどう", "gr_pe"), new("lemon", "レモン", "le_on"),
    ];

    private static ShellEnv Env() => new() { WordsJson = Words.Json(AllModeWords), Modes = [Japanese, Audio, Blank, Flash] };

    private static QuestionViewModel StartManual(ShellEnv env, ShellViewModel shell, Core.Models.LearningMode mode, int count = 2)
    {
        var home = shell.As<HomeViewModel>();
        shell.Run(env.Time, s => ((HomeViewModel)s).ToggleFreeChoiceCommand);
        home.SelectedMode = home.ModeOptions.First(o => !o.AutoMix && o.Mode == mode);
        home.SelectedQuestionCount = count;
        shell.Run(env.Time, s => ((HomeViewModel)s).StartCommand);
        return shell.As<QuestionViewModel>();
    }

    // ---- ホーム ----

    [Fact]
    public void Recommended_starts_with_one_command_and_reaches_result()
    {
        using var env = Env();
        env.Dir.Write(SettingsRepository.FileName, """{ "settingsVersion": 1, "practice": { "questionCount": 5, "weakWordsOnly": true, "autoMix": false } }""");
        var shell = env.Start();

        shell.Run(env.Time, s => ((HomeViewModel)s).RecommendedCommand);

        var answered = 0;
        while (shell.Current is QuestionViewModel)
        {
            shell.Current.BeginAccepting(); // フラッシュなら表示秒数を予約する
            env.Scheduler.RunAll(); // 表示秒数が過ぎたとみなす
            shell.AnswerCurrent(env.Time, correct: true);
            shell.Run(env.Time, s => ((FeedbackViewModel)s).NextCommand);
            answered++;
        }

        Assert.IsType<ResultViewModel>(shell.Current);
        Assert.Equal(5, answered);
    }

    [Fact]
    public void Free_choice_is_closed_at_first_and_toggles_and_changes_are_not_saved_as_defaults()
    {
        using var env = Env();
        var shell = env.Start();
        var home = shell.As<HomeViewModel>();
        Assert.False(home.IsFreeChoiceOpen);

        shell.Run(env.Time, s => ((HomeViewModel)s).ToggleFreeChoiceCommand);
        Assert.True(home.IsFreeChoiceOpen);
        home.SelectedQuestionCount = 5;
        home.SelectedMode = home.ModeOptions.First(o => o.Mode == Flash && !o.AutoMix);
        home.SelectedFlashSeconds = 2.0;
        shell.Run(env.Time, s => ((HomeViewModel)s).StartCommand);

        Assert.IsType<QuestionViewModel>(shell.Current);
        Assert.Null(env.Dir.Read(SettingsRepository.FileName)); // 基本設定には書かない
        var saved = Core.Application.ProgressJson.Parse(env.ProgressJsonText!).Session!;
        Assert.Equal(2.0, saved.Settings.Practice.FlashSeconds);
        Assert.False(saved.Settings.Practice.AutoMix);
    }

    [Fact]
    public void Recommended_returning_home_with_no_words_keeps_free_choice_closed()
    {
        // 克服の対象モード(日本語・音声)で出せる単語が無い → 0語
        using var env = new ShellEnv
        {
            WordsJson = Words.Json(new TestWord("apple", Japanese: false, Audio: false), new TestWord("banana", Japanese: false, Audio: false)),
            Modes = [Japanese, Audio, Blank, Flash],
        };
        var shell = env.Start();

        shell.Run(env.Time, s => ((HomeViewModel)s).RecommendedCommand);

        var home = shell.As<HomeViewModel>();
        Assert.NotNull(home.InfoMessage); // 0語の理由
        Assert.False(home.IsFreeChoiceOpen); // 触っていない自由選択は開かない
    }

    [Fact]
    public void Free_choice_returning_home_with_no_words_stays_open()
    {
        using var env = Env();
        var shell = env.Start();
        var home = shell.As<HomeViewModel>();
        shell.Run(env.Time, s => ((HomeViewModel)s).ToggleFreeChoiceCommand);
        home.WeakOnly = true; // 苦手が無いので0語

        shell.Run(env.Time, s => ((HomeViewModel)s).StartCommand);

        var back = shell.As<HomeViewModel>();
        Assert.NotNull(back.InfoMessage);
        Assert.True(back.IsFreeChoiceOpen);
        Assert.True(back.WeakOnly);
    }

    [Fact]
    public void Flash_seconds_are_offered_only_when_flash_can_appear()
    {
        using var env = Env();
        var home = env.Start().As<HomeViewModel>();

        Assert.True(home.IsFlashSecondsRelevant); // 自動ミックスでフラッシュが使える
        Assert.Equal([0.5, 1.0, 2.0], home.FlashSecondsOptions);
        home.SelectedMode = home.ModeOptions.First(o => o.Mode == Japanese && !o.AutoMix);
        Assert.False(home.IsFlashSecondsRelevant);
        home.SelectedMode = home.ModeOptions.First(o => o.Mode == Flash && !o.AutoMix);
        Assert.True(home.IsFlashSecondsRelevant);
    }

    [Fact]
    public void Device_without_audio_shows_reason_and_unavailable_audio_option()
    {
        using var env = new ShellEnv { Modes = [Japanese, Blank, Flash] };
        var home = env.Start().As<HomeViewModel>();

        Assert.NotNull(home.AudioUnavailableNote);
        var audio = Assert.Single(home.ModeOptions, o => o.Mode == Audio && !o.AutoMix);
        Assert.False(audio.Available);
    }

    [Fact]
    public void Device_with_audio_has_no_reason_and_normal_audio_option()
    {
        using var env = Env();
        var home = env.Start().As<HomeViewModel>();

        Assert.Null(home.AudioUnavailableNote);
        Assert.True(Assert.Single(home.ModeOptions, o => o.Mode == Audio && !o.AutoMix).Available);
    }

    // ---- 音声 ----

    [Fact]
    public void Audio_question_speaks_once_when_accepting_starts_and_hides_spelling_and_letter_count()
    {
        using var env = Env();
        var shell = env.Start();
        var q = StartManual(env, shell, Audio);

        Assert.Single(env.Speech.Spoken); // StartManual の Run が受け付けを開始した
        Assert.Equal(q.WordForTests, env.Speech.Spoken[0]);
        q.BeginAccepting();
        Assert.Single(env.Speech.Spoken); // 2回目の開始では読み直さない
        Assert.True(q.IsAudioMode);
        Assert.Null(q.LetterCount);
        Assert.DoesNotContain(q.WordForTests, q.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(q.WordForTests, q.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ctrl+Space", q.KeyHint, StringComparison.Ordinal);
    }

    [Fact]
    public void Replay_speaks_again_and_keeps_typed_input()
    {
        using var env = Env();
        var shell = env.Start();
        var q = StartManual(env, shell, Audio);
        q.Input = "app";

        shell.Run(env.Time, s => ((QuestionViewModel)s).ReplayCommand);
        shell.Run(env.Time, s => ((QuestionViewModel)s).ReplayCommand);

        Assert.Equal(3, env.Speech.Spoken.Count);
        Assert.Same(q, shell.Current);
        Assert.Equal("app", q.Input);
    }

    [Fact]
    public void Answering_stops_speech_and_next_audio_question_speaks_once()
    {
        using var env = Env();
        var shell = env.Start();
        StartManual(env, shell, Audio);

        shell.AnswerCurrent(env.Time, correct: true);
        Assert.Equal(1, env.Speech.Stops);
        Assert.Single(env.Speech.Spoken); // 判定画面では読まない
        shell.Run(env.Time, s => ((FeedbackViewModel)s).NextCommand);

        Assert.Equal(2, env.Speech.Spoken.Count);
    }

    [Fact]
    public void Speech_failure_shows_warning_logs_warn_without_word_and_still_allows_answer()
    {
        using var env = Env();
        env.Speech.FailNext = true;
        var shell = env.Start();
        var q = StartManual(env, shell, Audio);

        Assert.NotNull(q.SpeechWarning);
        var line = Assert.Single(env.Log.Lines, l => l.Op == "Speech");
        Assert.Equal(Logging.LogLevel.Warn, line.Level);
        Assert.DoesNotContain(q.WordForTests, line.Reason ?? "", StringComparison.OrdinalIgnoreCase);
        q.Input = q.WordForTests;
        Assert.True(q.AnswerCommand.CanExecute(null));
    }

    [Fact]
    public void Late_speech_failure_for_an_old_screen_is_ignored()
    {
        using var env = Env();
        env.Speech.DeferFailure = true;
        var shell = env.Start();
        var q = StartManual(env, shell, Audio);
        shell.AnswerCurrent(env.Time, correct: true); // 読み上げ中に答えて判定画面へ

        env.Speech.FireDeferred(); // 前の画面の読み上げの失敗が遅れて届く

        Assert.Null(q.SpeechWarning);
        Assert.DoesNotContain(env.Log.Lines, l => l.Op == "Speech");
        Assert.IsType<FeedbackViewModel>(shell.Current);
    }

    [Fact]
    public void Speech_warning_log_has_session_id()
    {
        using var env = Env();
        env.Speech.FailNext = true;
        var shell = env.Start();
        StartManual(env, shell, Audio);

        Assert.NotNull(Assert.Single(env.Log.Lines, l => l.Op == "Speech").SessionId);
    }

    [Fact]
    public void Audio_question_rebuilt_for_a_pending_save_does_not_speak_again()
    {
        using var env = Env();
        var shell = env.Start();
        StartManual(env, shell, Audio);
        env.Files.FailWrites = true;

        shell.Run(env.Time, s => ((QuestionViewModel)s).SkipCommand); // 後回しの保存に失敗 → 同じ問題の保存待ち
        shell.Run(env.Time, s => ((QuestionViewModel)s).RetrySaveCommand); // もう一度失敗

        Assert.True(shell.As<QuestionViewModel>().IsSavePending);
        Assert.Single(env.Speech.Spoken); // 作り直した画面では自動で読み直さない
    }

    [Fact]
    public void Non_audio_question_does_not_speak_and_has_no_replay()
    {
        using var env = Env();
        var shell = env.Start();
        var q = StartManual(env, shell, Japanese);

        Assert.Empty(env.Speech.Spoken);
        Assert.False(q.ReplayCommand.CanExecute(null));
    }

    // ---- フラッシュ ----

    [Fact]
    public void Flash_shows_word_hides_input_then_hides_word_after_seconds_and_focuses_input()
    {
        using var env = Env();
        var shell = env.Start();
        StartManual(env, shell, Flash); // 受け付け開始で ShowFlash まで進む

        var showing = shell.As<QuestionViewModel>();
        Assert.Equal(FlashPhase.Showing, showing.Flash);
        Assert.Equal(showing.WordForTests, showing.FlashWord);
        Assert.False(showing.IsInputVisible);
        Assert.False(showing.AnswerCommand.CanExecute(null));
        showing.BeginAccepting();
        Assert.Equal([TimeSpan.FromSeconds(1.0)], env.Scheduler.Delays); // 既定の1秒

        env.Scheduler.RunAll();

        var hidden = shell.As<QuestionViewModel>();
        Assert.Equal(FlashPhase.Hidden, hidden.Flash);
        Assert.Null(hidden.FlashWord);
        Assert.True(hidden.IsInputVisible);
        hidden.Input = hidden.WordForTests;
        Assert.True(hidden.AnswerCommand.CanExecute(null));
        Assert.DoesNotContain(hidden.WordForTests, hidden.Prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Flash_start_save_failure_waits_without_timer_and_retry_starts_timer_once()
    {
        using var env = Env();
        var shell = env.Start();
        var home = shell.As<HomeViewModel>();
        shell.Run(env.Time, s => ((HomeViewModel)s).ToggleFreeChoiceCommand);
        home.SelectedMode = home.ModeOptions.First(o => !o.AutoMix && o.Mode == Flash);
        home.BeginAccepting();
        env.Time.Advance(ShellExtensions.Open);
        home.StartCommand.Execute(null); // 出題画面になる(まだ受け付けを始めていない)
        env.Files.FailWrites = true;

        shell.Current.BeginAccepting(); // ShowFlash → 印の保存に失敗

        var pending = shell.As<QuestionViewModel>();
        Assert.True(pending.IsSavePending);
        Assert.Equal(FlashPhase.NotStarted, pending.Flash);
        Assert.Null(pending.FlashWord);
        Assert.Empty(env.Scheduler.Delays);
        pending.BeginAccepting(); // 保存待ちの画面は ShowFlash を呼ばない(呼ぶと Flow が例外)
        Assert.Same(pending, shell.Current);

        env.Files.FailWrites = false;
        shell.Run(env.Time, s => ((QuestionViewModel)s).RetrySaveCommand);

        var showing = shell.As<QuestionViewModel>();
        Assert.Equal(FlashPhase.Showing, showing.Flash);
        Assert.Single(env.Scheduler.Delays);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Quit_confirmation_during_flash_display_is_not_overtaken_by_the_timer(bool confirm)
    {
        using var env = Env();
        var shell = env.Start();
        StartManual(env, shell, Flash);
        var showing = shell.As<QuestionViewModel>();
        showing.BeginAccepting();
        env.Prompt.Answer = confirm;
        env.Prompt.WhileOpen = env.Scheduler.RunAll; // ダイアログの間に表示秒数が過ぎる

        shell.Run(env.Time, s => ((QuestionViewModel)s).QuitCommand);

        if (confirm)
        {
            Assert.IsType<ResumePromptViewModel>(shell.Current); // はい: やめられる(古い画面の操作として捨てられない)
        }
        else
        {
            var hidden = shell.As<QuestionViewModel>(); // いいえ: 単語を隠して入力欄へ
            Assert.Equal(FlashPhase.Hidden, hidden.Flash);
            Assert.True(hidden.IsInputVisible);
        }
    }

    [Fact]
    public void Leaving_flash_screen_cancels_timer_and_resume_does_not_show_word_again()
    {
        using var env = Env();
        env.Prompt.Answer = true;
        var shell = env.Start();
        StartManual(env, shell, Flash);
        var showing = shell.As<QuestionViewModel>();
        showing.BeginAccepting();
        var word = showing.WordForTests;

        shell.Run(env.Time, s => ((QuestionViewModel)s).QuitCommand); // 表示の途中でやめる
        Assert.Empty(env.Scheduler.Delays); // タイマーは取り消した
        shell.Run(env.Time, s => ((ResumePromptViewModel)s).ResumeCommand);

        var resumed = shell.As<QuestionViewModel>();
        Assert.Equal(word, resumed.WordForTests);
        Assert.Equal(FlashPhase.Hidden, resumed.Flash);
        Assert.Null(resumed.FlashWord); // 答えを再表示しない
        Assert.True(resumed.IsInputVisible);
    }
}
