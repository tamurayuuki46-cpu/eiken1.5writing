using System.Collections.Immutable;

using EikenSpelling.Core.Application;

namespace EikenSpelling.Flow;

/// <summary>
/// 語彙セットの閲覧(UI v0.3 S-06)。UnitSize は 50 / 100、Number は 1 始まり(語が無ければ 0)、Total はその単位のセット数。
/// Choice は開始の条件(前回選んだ値、無ければ保存済みの基本設定)。Rows の成績は WordId 別の記録の写しで、閲覧は何も書かない。
/// </summary>
public sealed record VocabularySetScreen(
    int UnitSize, int Number, int Total, ImmutableArray<VocabularyWordRow> Rows, int TotalWords) : FlowScreen
{
    public PracticeChoice Choice { get; init; } = PracticeChoiceApplier.Defaults;

    public ImmutableArray<int> QuestionCountChoices { get; init; } = [];

    public ImmutableArray<double> FlashSecondsChoices { get; init; } = [];

    public ImmutableArray<int> Units => VocabularySets.Units;

    public bool CanPrevious => Number > 1;

    public bool CanNext => Number < Total;

    /// <summary>このセットから練習を始められるか(語が無いセットは始められない)。</summary>
    public bool CanStart => Rows.Length > 0;
}

/// <summary>
/// セット限定の開始の確認(P0-AC v0.2 C 節)。Flow が確認の前に作った出題計画(Plan)を持つ(同意すると同じ Plan で始める)。
/// Available が Desired より少なければ「N問で開始」の確認、0 なら理由と条件変更だけ(CanStart が偽)。
/// SaveFailure は開始の保存に失敗したとき(同じ SessionId・Plan で保存し直す)。Stale は、確認の前提が変わって Plan を作り直したとき。
/// Plan は出題の語を含む(音声・フラッシュでは答えになる)ので、画面に出さず、ログにも出さない。
/// </summary>
public sealed record SetStartPreviewScreen(
    int Desired,
    int Available,
    ImmutableArray<PlannedQuestion> Plan,
    int UnitSize,
    int Number,
    int Total,
    SelectionEmptyReason EmptyReason) : FlowScreen
{
    public bool CanStart => Available > 0;

    public bool IsShort => Available < Desired;

    public string? SaveFailure { get; init; }

    public bool Stale { get; init; }

    public override string ToString() =>
        $"SetStartPreviewScreen {{ Desired = {Desired}, Available = {Available}, Set = {Number}/{Total}({UnitSize}), Stale = {Stale}, Plan = (伏せる) }}";
}
