using EikenSpelling.Core.Models;

namespace EikenSpelling.Core.Domain;

/// <summary>単語の最後の誤答(苦手度の「直近の誤答」と苦手一覧の「最後の誤答」の正本)。</summary>
public sealed record LatestMistake(LearningMode Mode, DateTimeOffset At, string? Input)
{
    /// <summary>
    /// モードの記録のうち LastWrongAt の時点(UTC で比べる)が最も新しいもの。再練習の誤答も含む。
    /// 同じ時点ならモードの順(日本語 → 音声 → 穴あき → フラッシュ)で先のもの。誤答が無ければ null。
    /// </summary>
    public static LatestMistake? Of(WordProgress word)
    {
        ArgumentNullException.ThrowIfNull(word);
        LatestMistake? best = null;
        foreach (var mode in Enum.GetValues<LearningMode>())
        {
            if (word.Mode(mode) is { LastWrongAt: { } at } record && (best is null || at.UtcTicks > best.At.UtcTicks))
            {
                best = new LatestMistake(mode, at, record.LastWrongInput);
            }
        }

        return best;
    }

    /// <summary>
    /// 利用者が実際に打った誤答のうち最新(各モードの実入力の時刻 = LastWrongInputAt ?? LastWrongAt で比べる)。
    /// 「答えを見る」で LastWrongAt だけが新しくなっていても、前の実入力をそのまま返す。実入力が無ければ null。
    /// 同じ時点ならモードの順で先のもの。
    /// </summary>
    public static LatestMistake? TypedInputOf(WordProgress word)
    {
        ArgumentNullException.ThrowIfNull(word);
        LatestMistake? best = null;
        foreach (var mode in Enum.GetValues<LearningMode>())
        {
            if (word.Mode(mode) is { LastWrongInput: { } input, TypedInputAt: { } at }
                && (best is null || at.UtcTicks > best.At.UtcTicks))
            {
                best = new LatestMistake(mode, at, input);
            }
        }

        return best;
    }
}
