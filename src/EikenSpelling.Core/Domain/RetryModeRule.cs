using EikenSpelling.Core.Models;

namespace EikenSpelling.Core.Domain;

/// <summary>追加再練習のモード(v1.1 9.1、2026-11-13 決定)。</summary>
public static class RetryModeRule
{
    /// <summary>手動は元のモード。自動は優先順で、元以外かつ使える最初のもの。無ければ元のモード。</summary>
    public static LearningMode Choose(LearningMode original, IReadOnlyCollection<LearningMode> eligibleModes, bool autoMix)
    {
        if (!autoMix)
        {
            return original;
        }

        // 優先順は LearningMode の並び(日本語 → 音声 → 穴あき → フラッシュ)。端数配分の同点時と同じ正本
        foreach (var mode in Enum.GetValues<LearningMode>())
        {
            if (mode != original && eligibleModes.Contains(mode))
            {
                return mode;
            }
        }

        return original;
    }
}
