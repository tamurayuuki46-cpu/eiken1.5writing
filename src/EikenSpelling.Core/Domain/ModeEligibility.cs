using System.Collections.Immutable;

using EikenSpelling.Core.Models;

namespace EikenSpelling.Core.Domain;

/// <summary>単語ごとの使えるモード(v1.1 6 章・8.4)。克服対象モード(MasteryRules)が1つも無ければ出せない(空を返す)。</summary>
public static class ModeEligibility
{
    /// <summary>並びは LearningMode の並び(日本語 → 音声 → 穴あき → フラッシュ)に固定。</summary>
    public static ImmutableArray<LearningMode> For(WordEntry word, IReadOnlySet<LearningMode> availableModes)
    {
        ArgumentNullException.ThrowIfNull(word);
        ArgumentNullException.ThrowIfNull(availableModes);
        var builder = ImmutableArray.CreateBuilder<LearningMode>(4);
        void Add(LearningMode mode, bool wordAllows)
        {
            if (wordAllows && availableModes.Contains(mode))
            {
                builder.Add(mode);
            }
        }

        Add(LearningMode.Japanese, word.JapaneseEligible && !string.IsNullOrWhiteSpace(word.Ja));
        Add(LearningMode.Audio, word.AudioEligible);
        Add(LearningMode.Blank, !string.IsNullOrWhiteSpace(word.Blanks));
        Add(LearningMode.Flash, true);

        return builder.Any(MasteryRules.CountsForMastery) ? builder.ToImmutable() : [];
    }
}
