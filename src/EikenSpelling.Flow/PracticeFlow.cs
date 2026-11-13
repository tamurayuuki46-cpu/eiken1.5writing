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
public sealed class PracticeFlow
{
    private const string SaveFailedMessage = "保存できませんでした。もう一度保存してください。直らないときはアプリを終了してください(最後に保存できた問題から再開できます)。";

    private readonly FlowOptions _options;
    private LearningSettings _settings = DefaultSettings.Create();
    private WordSet? _wordSet;
    private ImmutableDictionary<string, WordEntry> _words = ImmutableDictionary<string, WordEntry>.Empty;
    private ProgressRepository? _repository;
    private ProgressDocument _document = new();
    private SessionAnswerResult? _lastAnswer;

    private PracticeFlow(FlowOptions options)
    {
        _options = options;
        Screen = new HomeScreen();
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
            Screen = new BothCorruptScreen("新しい記録を作れませんでした。もう一度試すか、アプリを終了してください。");
            return;
        }

        _document = WithVersions(new ProgressDocument());
        Screen = new HomeScreen { Notices = ["新しい記録で始めました。壊れたファイルは残してあります。"] };
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
            Screen = prompt with { Message = "前回の練習を終えられませんでした(保存できません)。もう一度試してください。", Notices = [] };
            return;
        }

        _document = discarded;
        Screen = new HomeScreen();
    }

    public void StartPractice(PracticeChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        Require<HomeScreen>();
        var settings = PracticeChoiceApplier.Apply(_settings, choice);
        var now = _options.Time.GetUtcNow();
        var selection = QuestionSelector.Select(_wordSet!, _document.Progress, settings, _options.AvailableModes, now, _options.Random);
        if (selection.Plan.IsEmpty)
        {
            Screen = new HomeScreen(selection.EmptyReason);
            return;
        }

        var session = SessionManager.Start(_options.NewSessionId(), now, selection.Plan, settings);
        var started = _document with { Session = session };
        if (!Save(started).Succeeded)
        {
            Screen = new HomeScreen(Message: "練習を始められませんでした(保存できません)。");
            return;
        }

        _document = started;
        Screen = QuestionScreenFor(session);
    }

    public void Answer(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Require<QuestionScreen>();
        var result = SessionManager.Answer(_document.Session!, _document.Progress, input, _options.Time.GetUtcNow());

        // 保存の成否にかかわらず先に反映する(失敗時は同じ文書を保存し直す)
        _document = _document with { Progress = result.Progress, Session = result.SessionToSave };
        _lastAnswer = result;
        var save = Save(_document);
        Screen = new FeedbackScreen(
            result.IsCorrect,
            result.CorrectWord,
            result.IsCorrect ? [] : [.. SpellingDiffService.Diff(result.CorrectWord, input)],
            result.Change,
            result.Completed,
            save.Succeeded,
            save.Succeeded ? null : SaveFailedMessage,
            result.Session.SessionId);
    }

    public void RetrySave()
    {
        var feedback = Require<FeedbackScreen>(s => !s.Saved);
        var save = Save(_document);
        Screen = feedback with { Saved = save.Succeeded, SaveFailure = save.Succeeded ? null : SaveFailedMessage };
    }

    public void Next()
    {
        Require<FeedbackScreen>(s => s.Saved);
        var last = _lastAnswer!;
        Screen = last.Completed ? new ResultScreen(SessionSummary.From(last.Session)) : QuestionScreenFor(_document.Session!);
    }

    /// <summary>練習をやめて再開の選択へ。回答ごとに保存済みなので何も書かない。</summary>
    public void Quit()
    {
        Require<QuestionScreen>();
        Screen = ResumePrompt(_document.Session!);
    }

    public void GoHome()
    {
        Require<ResultScreen>();
        Screen = new HomeScreen();
    }

    private void Load()
    {
        var startup = FlowStartup.Load(_options);
        _settings = startup.Settings;
        _wordSet = startup.WordSet;
        _words = startup.WordSet is null
            ? ImmutableDictionary<string, WordEntry>.Empty
            : startup.WordSet.Words
                .GroupBy(w => w.Id, StringComparer.Ordinal)
                .ToImmutableDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        _repository = startup.Repository;
        _document = startup.Document ?? new ProgressDocument();
        _lastAnswer = null;
        Screen = startup.Document?.Session is { } session
            ? ResumePrompt(session) with { Notices = startup.Screen.Notices }
            : startup.Screen;
    }

    private ResumePromptScreen ResumePrompt(PracticeSession session)
    {
        var reason = ResumeCheck.Check(session, _words, _options.AvailableModes);
        return new ResumePromptScreen(
            new ResumeSummary(session.StartedAt, session.Answers.Length, session.Questions.Length), reason is null, reason);
    }

    private QuestionScreen QuestionScreenFor(PracticeSession session)
    {
        var q = SessionManager.Current(session)
            ?? throw new InvalidOperationException("このセッションには次の問題がありません。");
        var entry = _words.GetValueOrDefault(q.WordId);
        return new QuestionScreen(
            new QuestionView(q.WordId, q.Word, q.Mode, entry?.Ja ?? "", entry?.Blanks, q.Attempt == AttemptType.Retry),
            session.Position + 1,
            session.Questions.Length,
            session.Settings.Practice.FlashSeconds);
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
