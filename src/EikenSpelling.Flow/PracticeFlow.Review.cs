using EikenSpelling.Core.Application;
using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;

namespace EikenSpelling.Flow;

/// <summary>
/// 結果・苦手診断・復習(.steering/20261115-result-weak-review): 後回しの「答えを見る」と、苦手一覧の出入り。
/// 苦手一覧は結果またはホームから開く。結果から開いたときは、その結果の画面(表示用の要約だけ。完了したセッションは文書に無い)を
/// 覚え、戻るで同じ結果を出す。開く・並べ替える・戻るでは何も書かない。
/// </summary>
public sealed partial class PracticeFlow
{
    private ResultScreen? _weakListReturn;

    /// <summary>
    /// 後回しの問題で「答えを見る」: 元の通常枠の不正解を1回確定し、判定の画面にする(今回の入力は無い)。
    /// 保存に失敗したら判定の画面のまま RetrySave だけを受け付ける(Reveal をもう一度呼ばない)。後回しの段階以外では受け付けない。
    /// </summary>
    public void Reveal()
    {
        Require<QuestionScreen>(s => s.CanReveal && !s.SavePending);
        Complete(SessionManager.Reveal(_document.Session!, _document.Progress, GradedAt()), input: null);
    }

    /// <summary>ホームまたは結果から苦手一覧へ。何も書かない。結果から開いたら、戻るでその結果に戻る。</summary>
    public void OpenWeakList()
    {
        var result = Screen as ResultScreen;
        if (result is null)
        {
            Require<HomeScreen>();
        }

        _weakListReturn = result;
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

    /// <summary>苦手一覧を閉じて、開いた画面に戻る(結果からなら同じ結果、ホームからならホーム)。何も書かない。</summary>
    public void CloseWeakList()
    {
        Require<WeakListScreen>();
        var back = _weakListReturn;
        _weakListReturn = null;
        Screen = back is not null ? back : Home();
    }

    private WeakListScreen WeakList(WeakListSort sort, bool weakOnly) =>
        FlowViews.WeakList(_wordSet!, _document.Progress, _editor.Current, _options.Time.GetUtcNow(), sort, weakOnly) with
        {
            Defaults = _editor.Defaults,
            Origin = _weakListReturn is null ? WeakListOrigin.Home : WeakListOrigin.Result,
            HasReviewTargets = HasReviewTargets(),
        };

    private bool HasReviewTargets()
    {
        var today = JstClock.ToJstDate(_options.Time.GetUtcNow());
        return _document.Progress.Words.Values.Any(w =>
            w.State == LearningState.Weak || (w.State == LearningState.Mastered && ReviewScheduler.IsDue(w, today)));
    }
}
