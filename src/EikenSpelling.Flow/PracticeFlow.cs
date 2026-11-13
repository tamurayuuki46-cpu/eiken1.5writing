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
    private const string StartFailedMessage = "練習を始められませんでした(保存できません)。";
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

    /// <summary>ホームから苦手一覧へ。何も書かない。</summary>
    public void OpenWeakList()
    {
        Require<HomeScreen>();
        Screen = WeakList(WeakListSort.WeakFirst, weakOnly: false);
    }

    /// <summary>並べ方・絞り込みを変えて作り直す。前の理由・文言は消える。何も書かない。</summary>
    public void ChangeWeakList(WeakListSort sort, bool weakOnly)
    {
        Require<WeakListScreen>();
        Screen = WeakList(sort, weakOnly);
    }

    /// <summary>一覧から苦手のみの練習を始める(choice の苦手のみは常に真)。0語・保存失敗なら一覧のまま理由を出す。</summary>
    public void StartWeakPractice(PracticeChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        var list = Require<WeakListScreen>();
        var (empty, failure) = Begin(choice with { WeakWordsOnly = true });
        if (empty != SelectionEmptyReason.None)
        {
            Screen = list with { EmptyReason = empty, Message = null, Diagnostic = null };
        }
        else if (failure is not null)
        {
            Screen = list with { EmptyReason = SelectionEmptyReason.None, Message = StartFailedMessage, Diagnostic = failure.Reason };
        }
    }

    /// <summary>苦手一覧からホームへ。何も書かない。</summary>
    public void BackToHome()
    {
        Require<WeakListScreen>();
        Screen = Home();
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
            result.Session.SessionId)
        {
            Diagnostic = save.Reason,
        };
    }

    public void RetrySave()
    {
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

    /// <summary>練習をやめて再開の選択へ。回答ごとに保存済みなので何も書かない。</summary>
    public void Quit()
    {
        Require<QuestionScreen>();
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
        var settings = PracticeChoiceApplier.Apply(_settings, choice);
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

        _document = started;
        Screen = QuestionScreenFor(session);
        return (SelectionEmptyReason.None, null);
    }

    private HomeScreen Home(SelectionEmptyReason emptyReason = SelectionEmptyReason.None, string? message = null) =>
        FlowViews.Home(_wordSet, _document.Progress, _options.Time.GetUtcNow(), emptyReason, message);

    private WeakListScreen WeakList(WeakListSort sort, bool weakOnly) =>
        FlowViews.WeakList(_wordSet!, _document.Progress, _settings, _options.Time.GetUtcNow(), sort, weakOnly);

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
