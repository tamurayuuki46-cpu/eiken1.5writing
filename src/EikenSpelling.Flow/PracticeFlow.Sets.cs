using System.Collections.Immutable;

using EikenSpelling.Core.Application;
using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

namespace EikenSpelling.Flow;

/// <summary>
/// 語彙セットの閲覧と、セット限定の開始(.steering/20261115-vocabulary-sets)。
/// 閲覧(開く・送る・単位切替・閉じる)と確認(プレビュー・取消)は何も書かない。開始の保存だけが書く。
/// 出題計画(SetPlan)は確認の前に1回だけ作り、同意では作り直さない(Selector を撃ち直さない)。
/// 前提(語彙版・使えるモード・設定・範囲の語)が変わっていたら古い確認を失効させ、新しい確認を出す(開始はしない)。
/// セット制限は SetPlan と session.Scope にだけあり、おすすめ・自由選択・苦手の一括復習(Begin)には持ち越さない。
/// </summary>
public sealed partial class PracticeFlow
{
    private ImmutableArray<WordEntry> _ordered = [];
    private int _setUnit = VocabularySets.BaseSize;
    private int _setNumber = 1;
    private PracticeChoice? _setChoice;
    private SetPlan? _setPlan;

    /// <summary>ホームから語彙セットの一覧へ。同じ起動の間は前回の単位とページを覚えている。</summary>
    public void OpenVocabularySets()
    {
        Require<HomeScreen>();
        _setChoice = null; // 開始条件は今回だけ: 開くたびに保存済みの基本設定から
        Screen = SetScreen();
    }

    public void NextSet()
    {
        Require<VocabularySetScreen>(s => s.CanNext);
        _setNumber++;
        Screen = SetScreen();
    }

    public void PreviousSet()
    {
        Require<VocabularySetScreen>(s => s.CanPrevious);
        _setNumber--;
        Screen = SetScreen();
    }

    /// <summary>表示単位(50 / 100)を切り替える。元のページの先頭の語を含むページを選ぶ。</summary>
    public void ChangeSetUnit(int unit)
    {
        var current = Require<VocabularySetScreen>();
        if (!VocabularySets.IsSupportedUnit(unit))
        {
            throw new ArgumentOutOfRangeException(nameof(unit), unit, "表示単位は 50 語か 100 語だけです。");
        }

        _setNumber = current.Rows.IsEmpty ? 1 : VocabularySets.NumberContaining(_ordered, unit, current.Rows[0].WordId);
        _setUnit = unit;
        Screen = SetScreen();
    }

    public void CloseVocabularySets()
    {
        Require<VocabularySetScreen>();
        Screen = Home();
    }

    /// <summary>
    /// 今のセットで練習を始める準備(出題計画を1回作る)。満数ならそのまま始め、足りなければ確認(0 なら理由だけ)の画面にする。
    /// どちらでも、開始の保存より前には何も書かない。
    /// </summary>
    public void PreviewSetPractice(PracticeChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        var set = Require<VocabularySetScreen>(s => s.CanStart);
        var plan = BuildSetPlan(choice, set.UnitSize, set.Number); // 例外なら状態は変わらない
        _setChoice = choice;
        _setPlan = plan;
        if (plan.Plan.IsEmpty || plan.Plan.Length < choice.QuestionCount)
        {
            Screen = PreviewScreen(plan, stale: false);
            return;
        }

        StartSetPlan(plan);
    }

    /// <summary>
    /// 確認に同意して、確認前に作った同じ Plan で始める(Selector は撃ち直さない)。開始の保存に失敗したら同じ画面で、
    /// もう一度呼ぶと同じ SessionId・Plan を保存し直す。前提が変わっていたら、新しい確認を出すだけで始めない(Stale)。
    /// </summary>
    public void ConfirmSetStart()
    {
        Require<SetStartPreviewScreen>(s => s.CanStart);
        var plan = _setPlan ?? throw new InvalidOperationException("確認中の出題計画がありません。");
        if (IsStale(plan))
        {
            RefreshStalePlan(plan);
            return;
        }

        StartSetPlan(plan);
    }

    /// <summary>「条件に戻る」: 計画を捨てて同じセットの一覧へ(何も書かない)。</summary>
    public void CancelSetStart()
    {
        Require<SetStartPreviewScreen>();
        _setPlan = null;
        Screen = SetScreen();
    }

    private VocabularySetScreen SetScreen()
    {
        var total = VocabularySets.SetCount(_ordered.Length, _setUnit);
        _setNumber = total == 0 ? 0 : Math.Clamp(_setNumber, 1, total);
        var today = JstClock.ToJstDate(_options.Time.GetUtcNow());
        var rows = total == 0
            ? []
            : VocabularySets.Rows(VocabularySets.Get(_ordered, _setUnit, _setNumber), _document.Progress, today);
        return new VocabularySetScreen(_setUnit, _setNumber, total, rows, _ordered.Length)
        {
            Choice = _setChoice ?? _editor.Defaults,
            QuestionCountChoices = [.. _editor.Current.Practice.QuestionCountChoices],
            FlashSecondsChoices = [.. _editor.Current.Practice.FlashSecondsChoices],
        };
    }

    private SetPlan BuildSetPlan(PracticeChoice choice, int unit, int number)
    {
        var scope = VocabularySetScope.From(VocabularySets.Get(_ordered, unit, number), _wordSet!.WordSetVersion);
        var settings = PracticeChoiceApplier.Apply(_editor.Current, choice);
        var selection = QuestionSelector.Select(
            _wordSet, _document.Progress, settings, _options.AvailableModes, _options.Time.GetUtcNow(), _options.Random, scope.ToSet());
        return new SetPlan(
            scope, choice, settings, selection.Plan, selection.EmptyReason, [.. _options.AvailableModes], LearningSettingsJson.Serialize(_editor.Current));
    }

    private SetStartPreviewScreen PreviewScreen(SetPlan plan, bool stale) =>
        new(plan.Choice.QuestionCount, plan.Plan.Length, plan.Plan, plan.Scope.UnitSize, plan.Scope.Number, plan.Scope.TotalSets, plan.EmptyReason)
        {
            Stale = stale,
        };

    /// <summary>確認の前提が、計画を作ったときから変わったか(語彙版・使えるモード・設定・範囲の語)。</summary>
    private bool IsStale(SetPlan plan)
    {
        if (plan.Scope.WordSetVersion != _wordSet!.WordSetVersion
            || !plan.AvailableModes.SetEquals(_options.AvailableModes)
            || plan.SettingsFingerprint != LearningSettingsJson.Serialize(_editor.Current))
        {
            return true;
        }

        var total = VocabularySets.SetCount(_ordered.Length, plan.Scope.UnitSize);
        return plan.Scope.Number > total
            || !VocabularySets.Get(_ordered, plan.Scope.UnitSize, plan.Scope.Number).Words.Select(w => w.Id).SequenceEqual(plan.Scope.WordIds, StringComparer.Ordinal);
    }

    /// <summary>古い確認を失効させる。同じ範囲が今もあれば作り直した計画を確認として出し(開始しない)、無ければ一覧へ戻す。</summary>
    private void RefreshStalePlan(SetPlan old)
    {
        if (old.Scope.Number > VocabularySets.SetCount(_ordered.Length, old.Scope.UnitSize))
        {
            _setPlan = null;
            Screen = SetScreen();
            return;
        }

        var fresh = BuildSetPlan(old.Choice, old.Scope.UnitSize, old.Scope.Number);
        _setPlan = fresh;
        Screen = PreviewScreen(fresh, stale: true);
    }

    /// <summary>
    /// 計画から1つのセッションを作って保存する。候補の文書(SessionId・開始時刻を含む)は最初の1回だけ作って計画に持たせ、
    /// 保存に失敗したら同じ画面のまま、再保存では同じ候補を使う(二重のセッションを作らない)。
    /// </summary>
    private void StartSetPlan(SetPlan plan)
    {
        if (plan.Candidate is null)
        {
            var session = SessionManager.Start(_options.NewSessionId(), _options.Time.GetUtcNow(), plan.Plan, plan.Settings, plan.Scope);
            plan = plan with { Candidate = _document with { Session = session } };
            _setPlan = plan;
        }

        var saved = Save(plan.Candidate!);
        if (!saved.Succeeded)
        {
            Screen = PreviewScreen(plan, stale: false) with { SaveFailure = StartFailedMessage, Diagnostic = saved.Reason };
            return;
        }

        var question = QuestionScreenFor(plan.Candidate!.Session!); // 先に作る(例外なら文書も画面も変えない)
        _document = plan.Candidate;
        _setPlan = null;
        _weakListReturn = null;
        Screen = question;
    }

    /// <summary>確認の前に作った出題計画。Candidate は開始の保存のために1回だけ作った文書(再保存で同じものを使う)。</summary>
    private sealed record SetPlan(
        VocabularySetScope Scope,
        PracticeChoice Choice,
        LearningSettings Settings,
        ImmutableArray<PlannedQuestion> Plan,
        SelectionEmptyReason EmptyReason,
        ImmutableHashSet<LearningMode> AvailableModes,
        string SettingsFingerprint)
    {
        public ProgressDocument? Candidate { get; init; }
    }
}
