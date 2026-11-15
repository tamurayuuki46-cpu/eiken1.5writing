using System.Collections.Immutable;

namespace EikenSpelling.Core.Application;

/// <summary>
/// セッションが開始時に固定した語彙セットの範囲(P0-AC v0.2 C 節)。許可する WordId の集合(表示順)と、開始時の単位・番号・総数・語彙版を持つ。
/// 範囲の無いセッション(従来の通常学習)は PracticeSession.Scope が null で、ここの「空」とは別の意味を持つ(空は不正)。
/// 再開は現在のセット番号から選び直さず、ここに保存した集合と出題の列で検査する。
/// </summary>
public sealed record VocabularySetScope(int UnitSize, int Number, int TotalSets, string WordSetVersion, ImmutableArray<string> WordIds)
{
    public static VocabularySetScope From(VocabularySet set, string wordSetVersion)
    {
        ArgumentNullException.ThrowIfNull(set);
        return new VocabularySetScope(set.UnitSize, set.Number, set.Total, wordSetVersion, [.. set.Words.Select(w => w.Id)]);
    }

    /// <summary>形が正しければ null、問題があれば理由(文言に単語の内容を含めない)。</summary>
    public string? Problem()
    {
        if (!VocabularySets.IsSupportedUnit(UnitSize))
        {
            return "語彙セットの表示単位が不正です。";
        }

        if (TotalSets < 1 || Number < 1 || Number > TotalSets)
        {
            return "語彙セットの番号が範囲外です。";
        }

        if (WordSetVersion is null)
        {
            return "語彙セットの語彙版がありません。";
        }

        if (WordIds.IsDefaultOrEmpty)
        {
            return "語彙セットの範囲が空です。";
        }

        if (WordIds.Length > UnitSize)
        {
            return "語彙セットの範囲が表示単位より大きいです。";
        }

        if (WordIds.Any(string.IsNullOrWhiteSpace))
        {
            return "語彙セットの範囲に空の単語IDがあります。";
        }

        return WordIds.Distinct(StringComparer.Ordinal).Count() != WordIds.Length ? "語彙セットの範囲に同じ単語IDが2回あります。" : null;
    }

    /// <summary>範囲に含まれる ID の集合(Ordinal)。</summary>
    public HashSet<string> ToSet() => WordIds.IsDefault ? [] : WordIds.ToHashSet(StringComparer.Ordinal);
}
