using System.Collections.Immutable;

using EikenSpelling.Core.Application;
using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;
using EikenSpelling.Persistence;

namespace EikenSpelling.Flow;

/// <summary>
/// 画面の手前の進行役(app-flow design)。今の画面(Screen)を持ち、受け付けない操作は InvalidOperationException にする(状態は変えない)。
/// 回答は保存の前にメモリ上の文書へ反映する(SessionManager.Answer を同じ問題で二度呼ばない)。1スレッドから使う。
/// </summary>
public sealed partial class PracticeFlow
{
    private const string StartFailedMessage = "練習を始められませんでした(保存できません)。";
    private const string SaveFailedMessage = "保存できませんでした。もう一度保存してください。直らないときはアプリを終了してください(最後に保存できた問題から再開できます)。";

    private readonly FlowOptions _options;
    private SettingsEditor _editor = new(null, DefaultSettings.Create());
    private WordSet? _wordSet;
    private ImmutableDictionary<string, WordEntry> _words = ImmutableDictionary<string, WordEntry>.Empty;
    private ProgressRepository? _repository;
    private ProgressDocument _document = new();
    private SessionAnswerResult? _lastAnswer;

    private PracticeFlow(FlowOptions options)
    {
        _options = options;
        Screen = new HomeScreen(StudyStatus.Empty);
    }

    public FlowScreen Screen { get; private set; }

    public static PracticeFlow Start(FlowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var flow = new PracticeFlow(options);
        flow.Load();
        return flow;
    }

    /// <summary>学習記録を読めなかったとき(Unavailable)だけ。設定の読込からやり直す。</summary>
    public void Reload()
    {
        Require<FatalScreen>(s => s.CanReload);
        Load();
    }

    /// <summary>両方破損のとき、利用者が確認した後だけ。壊れたファイルは退避名で残る。</summary>
    public void StartFresh()
    {
        Require<BothCorruptScreen>();
        var result = _repository!.StartFresh(_wordSet!.WordSetVersion, _wordSet.SourceVersion);
        if (!result.Succeeded)
        {
            Screen = new BothCorruptScreen("新しい記録を作れませんでした。もう一度試すか、アプリを終了してください。") { Diagnostic = result.Reason };
            return;
        }

        _document = WithVersions(new ProgressDocument());
        Screen = Home() with { Notices = ["新しい記録で始めました。壊れたファイルは残してあります。"] };
    }

    public void Resume()
    {
        Require<ResumePromptScreen>(s => s.CanResume);
        Screen = QuestionScreenFor(_document.Session!);
    }

    /// <summary>未完了の練習を終える。回答済みの記録は残す。保存に失敗したら再開の選択のまま。</summary>
    public void DiscardAndGoHome()
    {
        var prompt = Require<ResumePromptScreen>();
        var discarded = _document with
        {
            Progress = SessionManager.Abandon(_document.Session!, _document.Progress),
            Session = null,
        };
        var result = Save(discarded);
        if (!result.Succeeded)
        {
            Screen = prompt with
            {
                Message = "前回の練習を終えられませんでした(保存できません)。もう一度試してください。",
                Notices = [],
                Diagnostic = result.Reason,
            };
            return;
        }

        _document = discarded;
        Screen = Home();
    }

    /// <summary>
    /// おすすめ学習(UI v0.3 S-02): 保存済みの問題数・自動ミックス ON・苦手のみ OFF で始める
    /// (手動モード・フラッシュ秒数は保存済みの値)。基本設定には書かない。
    /// </summary>
    public void StartRecommended() =>
        StartPractice(_editor.Defaults with { AutoMix = true, WeakWordsOnly = false });

    public void StartPractice(PracticeChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        Require<HomeScreen>();
        var (empty, failure) = Begin(choice);
        if (empty != SelectionEmptyReason.None)
        {
            Screen = Home(empty);
        }
        else if (failure is not null)
        {
            Screen = Home(message: StartFailedMessage) with { Diagnostic = failure.Reason };
        }
    }

    public void Answer(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Require<QuestionScreen>(s => s.CanAnswer);
        var result = SessionManager.Answer(_document.Session!, _document.Progress, input, GradedAt());
        Complete(result, input);
    }

    /// <summary>
    /// 採点の結果を文書へ反映して保存し、判定の画面にする(Answer と Reveal で共通)。保存の成否にかかわらず先に反映する
    /// (失敗時は同じ文書を保存し直す。SessionManager の Answer・Reveal を同じ問題で二度呼ばない)。
    /// input が null なら「答えを見る」(今回の入力は無いので差分を作らない)。
    /// </summary>
    private void Complete(SessionAnswerResult result, string? input)
    {
        _document = _document with { Progress = result.Progress, Session = result.SessionToSave };
        _lastAnswer = result;
        var save = Save(_document);
        Screen = new FeedbackScreen(
            result.IsCorrect,
            result.CorrectWord,
            result.IsCorrect || input is null ? [] : [.. SpellingDiffService.Diff(result.CorrectWord, input)],
            result.Change,
            result.Completed,
            save.Succeeded,
            save.Succeeded ? null : SaveFailedMessage,
            result.Session.SessionId)
        {
            Diagnostic = save.Reason,
            Revealed = result.Revealed,
        };
    }

    public void RetrySave()
    {
        if (Screen is QuestionScreen { SavePending: true })
        {
            SavePendingCandidate();
            return;
        }

        var feedback = Require<FeedbackScreen>(s => !s.Saved);
        var save = Save(_document);
        Screen = feedback with
        {
            Saved = save.Succeeded,
            SaveFailure = save.Succeeded ? null : SaveFailedMessage,
            Diagnostic = save.Reason,
        };
    }

    public void Next()
    {
        Require<FeedbackScreen>(s => s.Saved);
        var last = _lastAnswer!;
        Screen = last.Completed ? new ResultScreen(SessionSummary.From(last.Session)) : QuestionScreenFor(_document.Session!);
    }

    /// <summary>練習をやめて再開の選択へ。回答ごとに保存済みなので何も書かない(スキップの保存待ちの間はできない)。</summary>
    public void Quit()
    {
        Require<QuestionScreen>(s => !s.SavePending);
        Screen = ResumePrompt(_document.Session!);
    }

    public void GoHome()
    {
        Require<ResultScreen>();
        Screen = Home();
    }

    private void Load()
    {
        var startup = FlowStartup.Load(_options);
        _editor = new SettingsEditor(startup.SettingsRepository, startup.Settings);
        _wordSet = startup.WordSet;
        _words = startup.WordSet is null
            ? ImmutableDictionary<string, WordEntry>.Empty
            : startup.WordSet.Words
                .GroupBy(w => w.Id, StringComparer.Ordinal)
                .ToImmutableDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        _repository = startup.Repository;
        _document = startup.Document ?? new ProgressDocument();
        _lastAnswer = null;
        _pending = null;
        _ordered = startup.WordSet is null ? [] : VocabularySets.Order(startup.WordSet);
        _setUnit = VocabularySets.BaseSize;
        _setNumber = 1;
        _setChoice = null;
        _setPlan = null;
        // 最初の画面はここだけで決める: 止まる画面 / 未完了の練習があれば再開の選択 / それ以外はホーム
        FlowScreen first = startup.Stop
            ?? (startup.Document?.Session is { } session ? ResumePrompt(session) : Home());
        Screen = first with { Notices = startup.Notices };
    }

    /// <summary>
    /// 練習を始める(出題の選択 → 開始 → 保存)。始められたら出題の画面にして (None, null)。
    /// 0語なら理由、保存に失敗したらその結果を返し、画面と文書は変えない(戻る画面は呼び出し側が決める)。
    /// </summary>
    private (SelectionEmptyReason Empty, SaveResult? Failure) Begin(PracticeChoice choice)
    {
        var settings = PracticeChoiceApplier.Apply(_editor.Current, choice);
        var now = _options.Time.GetUtcNow();
        var selection = QuestionSelector.Select(_wordSet!, _document.Progress, settings, _options.AvailableModes, now, _options.Random);
        if (selection.Plan.IsEmpty)
        {
            return (selection.EmptyReason, null);
        }

        var session = SessionManager.Start(_options.NewSessionId(), now, selection.Plan, settings);
        var started = _document with { Session = session };
        var saved = Save(started);
        if (!saved.Succeeded)
        {
            return (SelectionEmptyReason.None, saved);
        }

        var question = QuestionScreenFor(session); // 先に作る(例外なら文書も画面も変えない)
        _document = started;
        _weakListReturn = null;
        Screen = question;
        return (SelectionEmptyReason.None, null);
    }

    private HomeScreen Home(SelectionEmptyReason emptyReason = SelectionEmptyReason.None, string? message = null) =>
        FlowViews.Home(_wordSet, _document.Progress, _options.Time.GetUtcNow(), emptyReason, message) with
        {
            Defaults = _editor.Defaults,
            QuestionCountChoices = [.. _editor.Current.Practice.QuestionCountChoices],
            FlashSecondsChoices = [.. _editor.Current.Practice.FlashSecondsChoices],
            Today = Today(),
        };

    /// <summary>
    /// 採点の時刻(端末のローカル時刻)。日別の実績はこのオフセットの日付で数える(2026-11-15 本人確定)。
    /// 文書に入るので、保存失敗の再保存の時刻で別の日に移らない。
    /// </summary>
    private DateTimeOffset GradedAt() => _options.Time.GetLocalNow();

    /// <summary>今日の目標と実績(端末のローカル日付。画面を作るたびに求めるので、日をまたいでも戻れば当日の値)。</summary>
    private DailyProgress Today() =>
        DailyProgress.From(_document.Progress, DailyActivity.DayOf(_options.Time.GetLocalNow()), _editor.Current.Goal.DailyQuestionGoal);

    private ResumePromptScreen ResumePrompt(PracticeSession session)
    {
        var reason = ResumeCheck.Check(session, _words, _options.AvailableModes);
        var summary = new ResumeSummary(session.StartedAt, session.Answers.Length, session.Questions.Length)
        {
            Phase = SessionManager.Phase(session),
            Skipped = session.Skips.Length,
            Set = session.Scope is { } scope
                ? new ResumeSetContext(scope.UnitSize, scope.Number, scope.TotalSets, scope.WordSetVersion != _wordSet!.WordSetVersion)
                : null,
            Remaining = session.Questions.Length - session.Answers.Length,
            // 通常問題が残っていれば誤答で再練習が加わる。再練習の段階でも、2回目以降の再練習がありうる設定なら加わる
            MayAddRetries = (session.Answers.Length < session.NormalCount && session.Settings.Practice.RetryCount > 0)
                || (SessionManager.Phase(session) == QuestionPhase.Retry && session.Settings.Practice.RetryCount > 1),
            AutoMix = session.Settings.Practice.AutoMix,
            ManualMode = session.Settings.Practice.ManualMode,
            WeakOnly = session.Settings.Practice.WeakWordsOnly,
            NormalCount = session.NormalCount,
        };
        return new ResumePromptScreen(summary, reason is null, reason) { Today = Today() };
    }

    private SaveResult Save(ProgressDocument document) => _repository!.Save(WithVersions(document));

    private ProgressDocument WithVersions(ProgressDocument document) => document with
    {
        WordSetVersion = _wordSet!.WordSetVersion,
        SourceVersion = _wordSet.SourceVersion,
    };

    private T Require<T>(Func<T, bool>? condition = null)
        where T : FlowScreen
    {
        if (Screen is T screen && (condition is null || condition(screen)))
        {
            return screen;
        }

        throw new InvalidOperationException($"今の画面({Screen.GetType().Name})ではこの操作はできません。");
    }
}
