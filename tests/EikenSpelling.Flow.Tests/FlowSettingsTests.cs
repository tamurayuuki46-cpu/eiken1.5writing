using EikenSpelling.Core.Application;
using EikenSpelling.Core.Settings;
using EikenSpelling.Persistence;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Flow.Tests;

public class FlowSettingsTests
{
    private static readonly PracticeChoice Initial = FromSettings(DefaultSettings.Create());

    private static PracticeChoice FromSettings(LearningSettings s) =>
        new(s.Practice.AutoMix, s.Practice.ManualMode, s.Practice.QuestionCount, s.Practice.WeakWordsOnly, s.Practice.FlashSeconds);

    private static LearningSettings FileSettings(FlowEnv env) =>
        LearningSettingsJson.Parse(env.Dir.Read(SettingsRepository.FileName) ?? throw new InvalidOperationException("settings.json がありません。"));

    private static string[] CorruptFiles(FlowEnv env) =>
        env.Dir.Names().Where(n => n.StartsWith("settings.json.corrupt-", StringComparison.Ordinal)).ToArray();

    // ---- 初期値・保存・再起動 ----

    [Fact]
    public void First_run_home_defaults_are_the_initial_basic_settings()
    {
        using var env = new FlowEnv();

        Assert.Equal(Initial, env.Start().As<HomeScreen>().Defaults);
    }

    [Fact]
    public void Home_question_count_choices_come_from_settings()
    {
        using var env = new FlowEnv();

        Assert.Equal(DefaultSettings.Create().Practice.QuestionCountChoices, env.Start().As<HomeScreen>().QuestionCountChoices.ToArray());
    }

    [Fact]
    public void Home_question_count_choices_follow_a_changed_settings_file_on_every_home()
    {
        using var env = new FlowEnv();
        env.Dir.Write(SettingsRepository.FileName, """{ "settingsVersion": 1, "practice": { "questionCountChoices": [3, 7] } }""");
        var flow = env.Start();
        Assert.Equal([3, 7], flow.As<HomeScreen>().QuestionCountChoices.ToArray());

        flow.StartPractice(Choices.Auto(weakOnly: true)); // 苦手が無いので0語でホームに戻る
        var empty = flow.As<HomeScreen>();
        Assert.NotEqual(SelectionEmptyReason.None, empty.EmptyReason);
        Assert.Equal([3, 7], empty.QuestionCountChoices.ToArray());

        flow.StartPractice(Choices.Auto(count: 3));
        flow.AnswerAll(_ => true);
        flow.GoHome();
        Assert.Equal([3, 7], flow.As<HomeScreen>().QuestionCountChoices.ToArray());
    }

    [Fact]
    public void Saved_settings_become_defaults_and_survive_restart()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        var values = new PracticeChoice(false, Audio, 20, false, 2.0);

        flow.OpenSettings();
        Assert.Equal(Initial, flow.As<SettingsScreen>().Values);
        flow.SaveSettings(values);

        var home = flow.As<HomeScreen>();
        Assert.Equal(values, home.Defaults);
        Assert.NotNull(home.Message);
        Assert.Equal(values, FromSettings(FileSettings(env)));
        Assert.Equal(values, env.Start().As<HomeScreen>().Defaults);

        flow.StartPractice(home.Defaults);
        var practice = env.Progress().Session!.Settings.Practice;
        Assert.Equal(values, FromSettings(env.Progress().Session!.Settings));
        Assert.Equal(Audio, practice.ManualMode);
    }

    [Theory]
    [InlineData(1, 0.1)]
    [InlineData(100, 10.0)]
    [InlineData(7, 0.75)] // 選択肢に無い値も範囲内なら保存できる
    public void Values_within_limits_are_saved_exactly(int count, double seconds)
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        var values = new PracticeChoice(true, Japanese, count, false, seconds);
        flow.OpenSettings();

        flow.SaveSettings(values);

        var restarted = env.Start();
        Assert.Equal(values, restarted.As<HomeScreen>().Defaults);
        restarted.OpenSettings();
        var screen = restarted.As<SettingsScreen>();
        Assert.Equal(values, screen.Values);
        Assert.Equal([5, 10, 20], screen.QuestionCountChoices.ToArray()); // 選択肢は変わらない
    }

    public static TheoryData<int, double, int, bool, string> InvalidValues => new()
    {
        { 0, 1.0, 0, true, "practice.questionCount" },
        { 101, 1.0, 0, true, "practice.questionCount" },
        { 10, 0.09, 0, true, "practice.flashSeconds" },
        { 10, 10.01, 0, true, "practice.flashSeconds" },
        { 10, double.NaN, 0, true, "practice.flashSeconds" },
        { 10, double.PositiveInfinity, 0, true, "practice.flashSeconds" },
        { 10, -1, 0, true, "practice.flashSeconds" },
        { 10, 1.0, 99, true, "practice.manualMode" }, // 定義外のモードは自動ミックスでも不正
    };

    [Theory]
    [MemberData(nameof(InvalidValues))]
    public void Invalid_values_are_not_saved_and_keep_current_settings(int count, double seconds, int mode, bool autoMix, string path)
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.OpenSettings();
        flow.SaveSettings(new PracticeChoice(true, Audio, 20, false, 2.0)); // 今の設定
        var current = flow.As<HomeScreen>().Defaults;
        var before = env.Dir.Snapshot();
        flow.OpenSettings();
        var values = new PracticeChoice(autoMix, (Core.Models.LearningMode)mode, count, false, seconds);

        flow.SaveSettings(values);

        var screen = flow.As<SettingsScreen>();
        Assert.Contains(screen.Issues, i => i.Path == path);
        Assert.NotNull(screen.Message);
        Assert.Equal(values, screen.Values); // 入力した値は残る
        Assert.Equal(before, env.Dir.Snapshot());
        flow.CloseSettings();
        Assert.Equal(current, flow.As<HomeScreen>().Defaults);
        flow.OpenSettings();
        Assert.Equal(current, flow.As<SettingsScreen>().Values);
    }

    [Fact]
    public void Advanced_settings_are_kept_when_basic_settings_are_saved()
    {
        using var env = new FlowEnv();
        env.Dir.Write(SettingsRepository.FileName, """{ "settingsVersion": 1, "practice": { "retryCount": 2 }, "mastery": { "requiredCorrectDays": 3 } }""");
        var flow = env.Start();
        flow.OpenSettings();

        flow.SaveSettings(new PracticeChoice(false, Blank, 5, true, 0.5));

        var saved = FileSettings(env);
        Assert.Equal(2, saved.Practice.RetryCount);
        Assert.Equal(3, saved.Mastery.RequiredCorrectDays);
        Assert.Equal(5, saved.Practice.QuestionCount);
    }

    [Fact]
    public void Switching_to_auto_mix_with_zero_weights_reports_advanced_issue()
    {
        using var env = new FlowEnv();
        env.Dir.Write(SettingsRepository.FileName,
            """{ "settingsVersion": 1, "practice": { "autoMix": false, "modeWeights": { "japanese": 0, "audio": 0, "blank": 0, "flash": 0 } } }""");
        var flow = env.Start();
        Assert.Empty(flow.As<HomeScreen>().Notices); // 手動なので正しい設定
        flow.OpenSettings();
        var before = env.Dir.Snapshot();

        flow.SaveSettings(flow.As<SettingsScreen>().Values with { AutoMix = true });

        Assert.Contains(flow.As<SettingsScreen>().Issues, i => i.Path == "practice.modeWeights");
        Assert.Equal(before, env.Dir.Snapshot());
    }

    // ---- 書き込み失敗と、失敗の後の操作 ----

    [Fact]
    public void Write_failure_stays_on_settings_with_input_and_retry_succeeds()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.OpenSettings();
        var values = new PracticeChoice(false, Audio, 20, false, 2.0);
        env.Files.FailWrites = true;

        flow.SaveSettings(values);

        var screen = flow.As<SettingsScreen>();
        Assert.Equal(values, screen.Values);
        Assert.NotNull(screen.Message);
        Assert.NotNull(screen.Diagnostic);
        Assert.Empty(screen.Issues);
        Assert.DoesNotContain(env.Dir.Path, screen.Diagnostic, StringComparison.Ordinal);
        Assert.Null(env.Dir.Read(SettingsRepository.FileName));

        env.Files.FailWrites = false;
        flow.SaveSettings(values);
        Assert.Equal(values, flow.As<HomeScreen>().Defaults);
    }

    [Fact]
    public void Write_failure_keeps_existing_settings_file_unchanged()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.OpenSettings();
        flow.SaveSettings(Initial with { QuestionCount = 5 });
        flow.OpenSettings();
        var before = env.Dir.Snapshot();
        env.Files.FailWrites = true;

        flow.SaveSettings(Initial with { QuestionCount = 20 });

        Assert.IsType<SettingsScreen>(flow.Screen);
        Assert.Equal(before, env.Dir.Snapshot());
        env.Files.FailWrites = false;
        flow.CloseSettings();
        Assert.Equal(5, flow.As<HomeScreen>().Defaults.QuestionCount);
    }

    [Fact]
    public void Write_failure_then_close_keeps_current_settings()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.OpenSettings();
        env.Files.FailWrites = true;
        flow.SaveSettings(new PracticeChoice(false, Audio, 20, false, 2.0));
        env.Files.FailWrites = false;

        flow.CloseSettings();

        Assert.Equal(Initial, flow.As<HomeScreen>().Defaults);
        flow.OpenSettings();
        Assert.Equal(Initial, flow.As<SettingsScreen>().Values);
    }

    [Fact]
    public void Messages_are_replaced_by_the_latest_result()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.OpenSettings();
        var invalid = Initial with { QuestionCount = 0 };

        flow.SaveSettings(invalid); // Invalid
        var first = flow.As<SettingsScreen>();
        Assert.NotEmpty(first.Issues);

        env.Files.FailWrites = true;
        flow.SaveSettings(Initial with { QuestionCount = 20 }); // Failed: Issues は消える
        var failed = flow.As<SettingsScreen>();
        Assert.Empty(failed.Issues);
        Assert.NotEqual(first.Message, failed.Message);
        env.Files.FailWrites = false;

        flow.SaveSettings(invalid); // Invalid: 前の失敗の文言は残らない
        var again = flow.As<SettingsScreen>();
        Assert.NotEmpty(again.Issues);
        Assert.Equal(first.Message, again.Message);
        Assert.NotEqual(failed.Diagnostic, again.Diagnostic); // 前の失敗の理由は残らない

        flow.ResetSettings(); // 文言・Issues・Diagnostic が消える
        var reset = flow.As<SettingsScreen>();
        Assert.Empty(reset.Issues);
        Assert.Null(reset.Message);
        Assert.Null(reset.Diagnostic);

        flow.SaveSettings(Initial with { QuestionCount = 20 }); // Invalid の後でも保存できる
        Assert.Equal(20, flow.As<HomeScreen>().Defaults.QuestionCount);
    }

    // ---- 初期値に戻す・開く・閉じる ----

    [Fact]
    public void Reset_fills_initial_values_without_writing_and_save_writes_them()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.OpenSettings();
        flow.SaveSettings(new PracticeChoice(false, Audio, 20, true, 2.0));
        flow.OpenSettings();
        var before = env.Dir.Snapshot();

        flow.ResetSettings();

        Assert.Equal(Initial, flow.As<SettingsScreen>().Values);
        Assert.Equal(before, env.Dir.Snapshot());
        flow.SaveSettings(flow.As<SettingsScreen>().Values);
        Assert.Equal(Initial, FromSettings(FileSettings(env)));
    }

    [Fact]
    public void Open_reset_and_close_write_nothing_and_keep_progress()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.StartPractice(Choices.Auto(count: 1));
        flow.AnswerAll(_ => true);
        flow.GoHome();
        var before = env.Dir.Snapshot();

        flow.OpenSettings();
        flow.ResetSettings();
        flow.CloseSettings();

        Assert.Equal(before, env.Dir.Snapshot());
        var progress = env.ProgressJsonText;
        flow.OpenSettings();
        flow.SaveSettings(Initial with { QuestionCount = 20 });
        Assert.Equal(progress, env.ProgressJsonText); // 設定の保存は学習記録に触れない
    }

    // ---- 読込の状態ごと ----

    [Fact]
    public void Newer_settings_file_cannot_be_saved_but_can_be_reset_and_closed()
    {
        using var env = new FlowEnv();
        env.Dir.Write(SettingsRepository.FileName, """{ "settingsVersion": 99 }""");
        var flow = env.Start();
        flow.OpenSettings();
        var screen = flow.As<SettingsScreen>();
        var before = env.Dir.Snapshot();

        Assert.False(screen.CanSave);
        Assert.NotNull(screen.CannotSaveReason);
        Assert.Throws<InvalidOperationException>(() => flow.SaveSettings(Initial));
        Assert.Same(screen, flow.Screen);
        flow.ResetSettings();
        flow.CloseSettings();
        Assert.Equal(before, env.Dir.Snapshot());
    }

    [Fact]
    public void Main_corrupt_with_newer_backup_cannot_be_saved()
    {
        using var env = new FlowEnv();
        env.Dir.Write(SettingsRepository.FileName, "{ broken");
        env.Dir.Write("settings.bak.json", """{ "settingsVersion": 99 }""");
        var flow = env.Start();
        var before = env.Dir.Snapshot();

        flow.OpenSettings();

        Assert.False(flow.As<SettingsScreen>().CanSave);
        Assert.Throws<InvalidOperationException>(() => flow.SaveSettings(Initial));
        Assert.Equal(before, env.Dir.Snapshot());
    }

    [Fact]
    public void Unreadable_settings_cannot_be_saved_until_restart()
    {
        using var env = new FlowEnv();
        env.Dir.Write(SettingsRepository.FileName, """{ "settingsVersion": 1, "practice": { "questionCount": 20 } }""");
        env.Files.FailReadsOf.Add(SettingsRepository.FileName);
        var flow = env.Start();
        flow.OpenSettings();
        Assert.False(flow.As<SettingsScreen>().CanSave);
        Assert.Equal(Initial, flow.As<SettingsScreen>().Values); // 読めないので初期値
        var before = env.Dir.Snapshot();
        Assert.Throws<InvalidOperationException>(() => flow.SaveSettings(Initial));
        Assert.Equal(before, env.Dir.Snapshot());

        env.Files.FailReadsOf.Clear();
        var restarted = env.Start();
        restarted.OpenSettings();

        Assert.True(restarted.As<SettingsScreen>().CanSave);
        Assert.Equal(20, restarted.As<SettingsScreen>().Values.QuestionCount);
    }

    [Fact]
    public void Recovered_settings_show_backup_values_and_save_clears_the_notice()
    {
        using var env = new FlowEnv();
        env.Dir.Write(SettingsRepository.FileName, "{ broken");
        env.Dir.Write("settings.bak.json", """{ "settingsVersion": 1, "practice": { "questionCount": 5 } }""");
        var flow = env.Start();
        flow.OpenSettings();
        var screen = flow.As<SettingsScreen>();
        Assert.Equal(5, screen.Values.QuestionCount);
        Assert.True(screen.CanSave);

        flow.SaveSettings(screen.Values with { QuestionCount = 20 });

        Assert.Equal(20, flow.As<HomeScreen>().Defaults.QuestionCount);
        Assert.Equal(20, FileSettings(env).Practice.QuestionCount); // 起動時の復旧で作られた主ではなく、保存した値
        var restarted = env.Start().As<HomeScreen>();
        Assert.Empty(restarted.Notices);
        Assert.Equal(20, restarted.Defaults.QuestionCount);
    }

    [Fact]
    public void Corrupt_settings_warn_before_saving_and_keep_the_broken_file_once()
    {
        using var env = new FlowEnv();
        env.Dir.Write(SettingsRepository.FileName, "{ broken");
        var flow = env.Start();
        flow.OpenSettings();
        var screen = flow.As<SettingsScreen>();
        Assert.True(screen.CanSave);
        Assert.NotNull(screen.Note);

        flow.SaveSettings(screen.Values);

        var corrupt = Assert.Single(CorruptFiles(env));
        Assert.Equal("{ broken", env.Dir.Read(corrupt));
        Assert.Empty(env.Start().As<HomeScreen>().Notices);
        flow.OpenSettings();
        Assert.Null(flow.As<SettingsScreen>().Note); // 保存した後は正常なファイル
    }

    [Fact]
    public void Corrupt_settings_write_failure_then_retry_keeps_one_evacuated_file()
    {
        using var env = new FlowEnv();
        env.Dir.Write(SettingsRepository.FileName, "{ broken");
        var flow = env.Start();
        flow.OpenSettings();
        env.Files.FailWrites = true;

        flow.SaveSettings(Initial);

        Assert.IsType<SettingsScreen>(flow.Screen);
        env.Files.FailWrites = false;
        flow.SaveSettings(Initial with { QuestionCount = 5 });
        Assert.IsType<HomeScreen>(flow.Screen);
        var corrupt = Assert.Single(CorruptFiles(env));
        Assert.Equal("{ broken", env.Dir.Read(corrupt));
        Assert.Equal(5, FileSettings(env).Practice.QuestionCount);
    }

    [Fact]
    public void Reload_refreshes_settings_and_can_save()
    {
        using var env = new FlowEnv();
        env.Dir.Write(SettingsRepository.FileName, """{ "settingsVersion": 1, "practice": { "questionCount": 5 } }""");
        env.Dir.Write(ProgressRepository.FileName, FlowEnv.EmptyProgressJson);
        env.Files.FailReadsOf.Add(SettingsRepository.FileName);
        env.Files.FailReadsOf.Add(ProgressRepository.FileName);
        var flow = env.Start();
        Assert.IsType<FatalScreen>(flow.Screen);

        env.Files.FailReadsOf.Clear();
        flow.Reload();

        Assert.Equal(5, flow.As<HomeScreen>().Defaults.QuestionCount);
        flow.OpenSettings();
        Assert.True(flow.As<SettingsScreen>().CanSave);
    }

    [Fact]
    public void Home_after_start_fresh_has_defaults_and_settings_can_be_opened()
    {
        using var env = new FlowEnv();
        env.Dir.Write(SettingsRepository.FileName, """{ "settingsVersion": 1, "practice": { "questionCount": 5 } }""");
        env.Dir.Write(ProgressRepository.FileName, "{ broken");
        env.Dir.Write("progress.bak.json", "{ broken");
        var flow = env.Start();

        flow.StartFresh();

        Assert.Equal(5, flow.As<HomeScreen>().Defaults.QuestionCount);
        flow.OpenSettings();
        Assert.IsType<SettingsScreen>(flow.Screen);
    }

    // ---- 苦手一覧・手動モード ----

    [Fact]
    public void Weak_list_carries_saved_defaults()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        var values = new PracticeChoice(false, Japanese, 20, false, 0.5);
        flow.OpenSettings();
        flow.SaveSettings(values);

        flow.OpenWeakList();
        Assert.Equal(values, flow.As<WeakListScreen>().Defaults);
        flow.ChangeWeakList(WeakListSort.RecentMistake, weakOnly: true);
        Assert.Equal(values, flow.As<WeakListScreen>().Defaults);
    }

    [Fact]
    public void Manual_modes_list_only_available_modes_but_saved_unavailable_mode_is_kept()
    {
        using var env = new FlowEnv();
        var first = env.Start();
        first.OpenSettings();
        first.SaveSettings(new PracticeChoice(false, Audio, 5, false, 1.0));
        env.Modes = [Japanese, Blank, Flash]; // 音声の無い端末
        var flow = env.Start();

        flow.OpenSettings();
        var screen = flow.As<SettingsScreen>();

        Assert.Equal([Japanese, Blank, Flash], screen.ManualModes.ToArray());
        Assert.Equal(Audio, screen.Values.ManualMode);
        flow.SaveSettings(screen.Values); // そのまま保存できる
        var home = flow.As<HomeScreen>();
        flow.StartPractice(home.Defaults);
        Assert.Equal(SelectionEmptyReason.ManualModeUnavailable, flow.As<HomeScreen>().EmptyReason);
    }

    [Fact]
    public void Texts_do_not_contain_the_folder_path()
    {
        static IEnumerable<string?> Texts(SettingsScreen s) =>
            [s.CannotSaveReason, s.Note, s.Message, s.Diagnostic, .. s.Issues.Select(i => i.Message), .. s.Issues.Select(i => i.Path)];

        void Check(FlowEnv env, SettingsScreen s) =>
            Assert.All(Texts(s).OfType<string>(), t => Assert.DoesNotContain(env.Dir.Path, t, StringComparison.Ordinal));

        using (var newer = new FlowEnv())
        {
            newer.Dir.Write(SettingsRepository.FileName, """{ "settingsVersion": 99 }""");
            var flow = newer.Start();
            flow.OpenSettings();
            Check(newer, flow.As<SettingsScreen>());
        }

        using (var unreadable = new FlowEnv())
        {
            unreadable.Dir.Write(SettingsRepository.FileName, "{}");
            unreadable.Files.FailReadsOf.Add(SettingsRepository.FileName);
            var flow = unreadable.Start();
            flow.OpenSettings();
            Check(unreadable, flow.As<SettingsScreen>());
        }

        using var corrupt = new FlowEnv();
        corrupt.Dir.Write(SettingsRepository.FileName, "{ broken");
        var c = corrupt.Start();
        c.OpenSettings();
        Check(corrupt, c.As<SettingsScreen>()); // Note
        c.SaveSettings(Initial with { QuestionCount = 0 });
        Check(corrupt, c.As<SettingsScreen>()); // Invalid の Diagnostic・Issues
        corrupt.Files.FailWrites = true;
        c.SaveSettings(Initial);
        Check(corrupt, c.As<SettingsScreen>()); // 退避の上での書き込み失敗の Diagnostic
    }

    [Fact]
    public void Null_values_throw_and_keep_the_screen()
    {
        using var env = new FlowEnv();
        var flow = env.Start();
        flow.OpenSettings();
        var screen = flow.Screen;

        Assert.Throws<ArgumentNullException>(() => flow.SaveSettings(null!));

        Assert.Same(screen, flow.Screen);
    }
}
