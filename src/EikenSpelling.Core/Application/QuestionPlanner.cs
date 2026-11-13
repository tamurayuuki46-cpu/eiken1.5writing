using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

namespace EikenSpelling.Core.Application;

/// <summary>1回の練習の枠(目標値)。実際の出題は単語の不足・モード適格性によって減ることがある(Q21)。</summary>
public sealed record QuestionSlots(int NewWords, int WeakWords, int ReviewWords)
{
    public int Total => NewWords + WeakWords + ReviewWords;
}

/// <summary>
/// 設定に依存する出題枠の計算。単語の選び方そのものは QuestionSelectorが担当する。
/// </summary>
public static class QuestionPlanner
{
    /// <summary>
    /// 新規・苦手・再確認の枠(Q11・Q15・Q17)。新規は端数を切り上げ、再確認は苦手枠の一部と入れ替える。
    /// </summary>
    public static QuestionSlots PlanSlots(int questionCount, PracticeSettings practice, MasterySettings mastery)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(questionCount);

        var newWords = practice.WeakWordsOnly ? 0 : CeilingWithTolerance(questionCount * practice.NewWordRatio);
        newWords = Math.Clamp(newWords, 0, questionCount);
        var weakTotal = questionCount - newWords;
        var review = Math.Min(Math.Max(0, mastery.ReviewLimitFor(questionCount)), weakTotal);
        return new QuestionSlots(newWords, weakTotal - review, review);
    }

    /// <summary>
    /// 自動ミックスのモード配分(v1.1 8.2 最大剰余法)。使えないモードは除いて比率を計算し直す。
    /// 余りは小数部分の大きい順、同じなら 日本語 → 音声 → 穴あき → フラッシュ の順に配る。
    /// </summary>
    public static IReadOnlyDictionary<LearningMode, int> AllocateModes(
        int questionCount,
        ModeWeights weights,
        IReadOnlySet<LearningMode> availableModes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(questionCount);

        var result = Enum.GetValues<LearningMode>().ToDictionary(m => m, _ => 0);
        var candidates = Enum.GetValues<LearningMode>()
            .Where(m => availableModes.Contains(m) && weights[m] > 0)
            .ToList();
        // 重みが大きくても桁あふれしないよう long で計算する
        var totalWeight = candidates.Sum(m => (long)weights[m]);
        if (questionCount == 0 || totalWeight == 0)
        {
            return result;
        }

        // 整数演算で誤差を避ける: 理論値 = questionCount * weight / totalWeight
        var remainders = new List<(LearningMode Mode, long Remainder)>();
        var assigned = 0;
        foreach (var mode in candidates)
        {
            var numerator = (long)questionCount * weights[mode];
            result[mode] = (int)(numerator / totalWeight);
            assigned += result[mode];
            remainders.Add((mode, numerator % totalWeight));
        }

        foreach (var (mode, _) in remainders
                     .OrderByDescending(r => r.Remainder)
                     .ThenBy(r => (int)r.Mode)
                     .Take(questionCount - assigned))
        {
            result[mode]++;
        }

        return result;
    }

    private static int CeilingWithTolerance(double value) => (int)Math.Ceiling(value - 1e-9);
}
