using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;

namespace EikenSpelling.Flow;

/// <summary>
/// 出題画面での「採点しない進行」(後回し・フラッシュの表示開始)と、その保存待ち。
/// どちらも候補の文書を1つだけ作って持ち、保存できてから画面を進める。再保存ではその候補をそのまま使う(時刻・印を作り直さない)。
/// </summary>
public sealed partial class PracticeFlow
{
    private const string SkipSaveFailedMessage = "後回しを保存できませんでした。もう一度保存してください。直らないときはアプリを終了してください(この問題から再開できます)。";
    private const string FlashSaveFailedMessage = "フラッシュの表示を始められませんでした(保存できません)。もう一度保存してください。直らないときはアプリを終了してください(この問題から再開できます)。";

    private PendingSave? _pending;

    /// <summary>
    /// 今の通常問題を後回しにする(採点しない)。保存できたら次の問題へ。
    /// 失敗したら同じ問題に留まり、候補を持ったまま RetrySave だけを受け付ける。
    /// </summary>
    public void Skip()
    {
        Require<QuestionScreen>(s => s.CanSkip && !s.SavePending);
        var candidate = _document with { Session = SessionManager.Skip(_document.Session!, _options.Time.GetUtcNow()) };
        SavePendingCandidate(new PendingSave(candidate, PendingKind.Skip));
    }

    /// <summary>
    /// フラッシュの単語を見せ始める。表示開始の印を保存してから Showing にする(保存できなければ見せない)。
    /// </summary>
    public void ShowFlash()
    {
        Require<QuestionScreen>(s => s.Flash == FlashPhase.NotStarted && !s.SavePending);
        var candidate = _document with { Session = SessionManager.MarkFlashShown(_document.Session!) };
        SavePendingCandidate(new PendingSave(candidate, PendingKind.FlashStart));
    }

    /// <summary>フラッシュの表示秒数が過ぎた。単語を隠して入力欄にする(何も書かない。印は保存済み)。</summary>
    public void EndFlash()
    {
        Require<QuestionScreen>(s => s.Flash == FlashPhase.Showing);
        Screen = QuestionScreenFor(_document.Session!); // 印が今の提示なので Hidden になる(条件は QuestionScreenFor の1か所)
    }

    /// <summary>保持している候補を保存し直す(RetrySave から)。</summary>
    private void SavePendingCandidate() => SavePendingCandidate(_pending!);

    private void SavePendingCandidate(PendingSave pending)
    {
        var current = (QuestionScreen)Screen;
        var save = Save(pending.Document);
        if (!save.Succeeded)
        {
            _pending = pending;
            var message = pending.Kind == PendingKind.Skip ? SkipSaveFailedMessage : FlashSaveFailedMessage;
            Screen = current with { CanSkip = false, CanReveal = false, SaveFailure = message, Diagnostic = save.Reason };
            return;
        }

        // 先に作る(例外なら文書も画面も変えない)
        var next = QuestionScreenFor(pending.Document.Session!, flashShowing: pending.Kind == PendingKind.FlashStart);
        _document = pending.Document;
        _pending = null;
        Screen = next;
    }

    private QuestionScreen QuestionScreenFor(PracticeSession session, bool flashShowing = false)
    {
        var index = session.CurrentIndex
            ?? throw new InvalidOperationException("このセッションには次の問題がありません。");
        var q = session.Questions[index];
        var phase = SessionManager.Phase(session)!.Value;
        var (number, total) = phase switch
        {
            QuestionPhase.Normal => (index + 1, session.NormalCount),
            QuestionPhase.Deferred => (session.Skips.Select(s => s.QuestionIndex).ToList().IndexOf(index) + 1, session.Skips.Length),
            _ => (session.Answers.Length - session.NormalCount + 1, session.Questions.Length - session.Answers.Length),
        };
        var flash = q.Mode != LearningMode.Flash ? FlashPhase.None
            : !SessionManager.IsFlashShown(session) ? FlashPhase.NotStarted
            : flashShowing ? FlashPhase.Showing
            : FlashPhase.Hidden;
        var entry = _words.GetValueOrDefault(q.WordId);
        return new QuestionScreen(
            new QuestionView(q.WordId, q.Word, q.Mode, entry?.Ja ?? "", entry?.Blanks, q.Attempt == AttemptType.Retry),
            number,
            total,
            session.Settings.Practice.FlashSeconds)
        {
            Phase = phase,
            Flash = flash,

            // フラッシュは隠した後だけ後回しにできる(見る前・見ている間は答えの操作をしない)
            CanSkip = SessionManager.CanSkip(session) && flash is FlashPhase.None or FlashPhase.Hidden,
            CanReveal = SessionManager.CanReveal(session) && flash is FlashPhase.None or FlashPhase.Hidden,
            Answered = session.Answers.Length,
            SessionId = session.SessionId,
            QueueLength = session.Questions.Length,
        };
    }

    private enum PendingKind
    {
        Skip,
        FlashStart,
    }

    private sealed record PendingSave(ProgressDocument Document, PendingKind Kind);
}
