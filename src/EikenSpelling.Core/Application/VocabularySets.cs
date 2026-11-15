using System.Collections.Immutable;

using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;

namespace EikenSpelling.Core.Application;

/// <summary>語彙セットの1ページ。Number は 1 始まり、Total はその単位でのセット数。</summary>
public sealed record VocabularySet(int UnitSize, int Number, int Total, ImmutableArray<WordEntry> Words);

/// <summary>一覧の各語の学習状況(StudyStatus と同じ分類。ReviewDue は克服済みのうち再確認の日が来たもの)。</summary>
public enum WordStudyState
{
    Unseen,
    Normal,
    Weak,
    Mastered,
    ReviewDue,
}

/// <summary>一覧の1行。成績は WordId 別の記録だけから求める(50語用・100語用の成績は作らない)。</summary>
public sealed record VocabularyWordRow(string WordId, string Word, string Japanese, int Tier, int? FrequencyRank, WordStudyState State);

/// <summary>
/// 語彙セット(UI v0.3 S-06、P0-ST)。全語彙を PriorityTier 昇順 → 頻度順位ありを先に昇順 → WordId の Ordinal 順で整列し、
/// 50語を基本のセットにする。100語は先頭から隣接する50語2組(= 100語ずつの切り出しと同じ)で、末尾の端数も1セットとして残す。
/// 閲覧のための純粋な計算で、何も書き込まない。
/// </summary>
public static class VocabularySets
{
    /// <summary>基本のセットの語数。</summary>
    public const int BaseSize = 50;

    /// <summary>選べる表示単位(語数)。</summary>
    public static readonly ImmutableArray<int> Units = [BaseSize, BaseSize * 2];

    public static bool IsSupportedUnit(int unit) => Units.Contains(unit);

    /// <summary>整列した語(空・重複 ID は QuestionSelector と同じ規則で除く)。同じ入力の集合なら入力順に関係なく同じ並びになる。</summary>
    public static ImmutableArray<WordEntry> Order(WordSet wordSet)
    {
        ArgumentNullException.ThrowIfNull(wordSet);
        return
        [
            .. QuestionSelector.CleanWords(wordSet)
                .OrderBy(w => w.PriorityTier)
                .ThenBy(w => w.FrequencyRank.HasValue ? 0 : 1)
                .ThenBy(w => w.FrequencyRank)
                .ThenBy(w => w.Id, StringComparer.Ordinal),
        ];
    }

    /// <summary>その単位でのセット数(語が無ければ 0、ちょうど割り切れれば空の末尾セットは作らない)。</summary>
    public static int SetCount(int wordCount, int unit)
    {
        RequireUnit(unit);
        ArgumentOutOfRangeException.ThrowIfNegative(wordCount);
        return (wordCount + unit - 1) / unit;
    }

    public static VocabularySet Get(ImmutableArray<WordEntry> ordered, int unit, int number)
    {
        RequireUnit(unit);
        var total = SetCount(ordered.Length, unit);
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number, total);
        var start = (number - 1) * unit;
        return new VocabularySet(unit, number, total, [.. ordered.Skip(start).Take(unit)]);
    }

    /// <summary>
    /// その単位で、指定の語を含むセットの番号(単位の切り替えで元の先頭の語を含むページを選ぶため)。
    /// 語が無ければ 1(語彙更新で消えた語など)。
    /// </summary>
    public static int NumberContaining(ImmutableArray<WordEntry> ordered, int unit, string wordId)
    {
        RequireUnit(unit);
        for (var i = 0; i < ordered.Length; i++)
        {
            if (string.Equals(ordered[i].Id, wordId, StringComparison.Ordinal))
            {
                return (i / unit) + 1;
            }
        }

        return 1;
    }

    /// <summary>一覧の行。学習記録だけを読む。</summary>
    public static ImmutableArray<VocabularyWordRow> Rows(VocabularySet set, LearningProgress progress, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(progress);
        return
        [
            .. set.Words.Select(w => new VocabularyWordRow(w.Id, w.Word, w.Ja, w.PriorityTier, w.FrequencyRank, StateOf(progress.Word(w.Id), today))),
        ];
    }

    /// <summary>1語の学習状況(ホームの学習状況と一覧で共通。ReviewDue は克服済みのうち再確認の日が来たもの)。</summary>
    public static WordStudyState StateOf(WordProgress? record, DateOnly today) => record switch
    {
        null => WordStudyState.Unseen,
        { State: LearningState.Weak } => WordStudyState.Weak,
        { State: LearningState.Mastered } => ReviewScheduler.IsDue(record, today) ? WordStudyState.ReviewDue : WordStudyState.Mastered,
        _ => WordStudyState.Normal,
    };

    private static void RequireUnit(int unit)
    {
        if (!IsSupportedUnit(unit))
        {
            throw new ArgumentOutOfRangeException(nameof(unit), unit, "表示単位は 50 語か 100 語だけです。");
        }
    }
}
