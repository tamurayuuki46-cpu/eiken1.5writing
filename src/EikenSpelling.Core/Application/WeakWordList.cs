using System.Collections.Immutable;

using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

namespace EikenSpelling.Core.Application;

/// <summary>苦手一覧の並べ方(weak-list design 4.2)。</summary>
public enum WeakListSort
{
    /// <summary>苦手度の順(苦手は WeakWordScorer.Order、その後ろに克服済みを再確認日の早い順)。</summary>
    WeakFirst,

    /// <summary>最後の誤答が新しい順(誤答の無い単語は最後)。</summary>
    RecentMistake,
}

/// <summary>モード別の通常問題の正答率。通常問題で未出題は null(画面では「未学習」。v1.0 11.3)。</summary>
public sealed record ModeAccuracy(double? Japanese, double? Audio, double? Blank, double? Flash);

/// <summary>モード別の状態(行の展開に出す)。通常と再練習の件数を分ける。Accuracy は通常問題だけ(未出題は null)。</summary>
public sealed record ModeStat(LearningMode Mode, int NormalCorrect, int NormalWrong, int RetryCorrect, int RetryWrong, double? Accuracy);

/// <summary>
/// 苦手一覧の1行(v1.0 11.4)。苦手の項目(連続正答〜苦手度)は克服済みでは null、克服済みの項目(再確認日・再確認待ち)は苦手では null。
/// LastWrongInput は利用者が実際に打った最新の誤答(表示用。「答えを見る」は入力を作らないので、その不正解が新しくても前の入力のまま。
/// 無ければ null)。ログに出さないこと(ToString では伏せる)。モードと時刻は LastWrongInputMode・LastWrongInputAt。
/// LastWrongAt は最後の誤答(答えを見るを含む)の日本時間(直せない日時は null)。実入力の時刻とは別。
/// </summary>
public sealed record WeakWordRow(
    string WordId,
    string Word,
    bool InWordSet,
    LearningState State,
    int NormalMistakes,
    ModeAccuracy Accuracy,
    string? LastWrongInput,
    DateTimeOffset? LastWrongAt,
    int? MasteryStreak,
    int? RequiredCorrectDays,
    int? RemainingForMastery,
    bool? CountedToday,
    long? WeakScore,
    DateOnly? NextReviewDate,
    bool? ReviewDue)
{
    /// <summary>実入力のモード(LastWrongInput が無ければ null)。</summary>
    public LearningMode? LastWrongInputMode { get; init; }

    /// <summary>実入力の日時(日本時間。LastWrongInput が無い・直せない日時は null)。</summary>
    public DateTimeOffset? LastWrongInputAt { get; init; }

    /// <summary>モード別の状態(日本語・音声・穴あき・フラッシュの順にすべて)。</summary>
    public ImmutableArray<ModeStat> Modes { get; init; } = [];

    public override string ToString() => $"WeakWordRow {{ WordId = {WordId}, State = {State}, NormalMistakes = {NormalMistakes} }}";
}

/// <summary>苦手一覧(weak-list design 4.2)。学習記録は読むだけ。</summary>
public static class WeakWordList
{
    /// <summary>単語データは WordSetValidator を通したもの(ID の重複なし。重複があると例外)が前提。</summary>
    public static ImmutableArray<WeakWordRow> Build(
        LearningProgress progress, WordSet wordSet, LearningSettings settings, DateTimeOffset now, WeakListSort sort, bool weakOnly)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(wordSet);
        ArgumentNullException.ThrowIfNull(settings);
        if (!Enum.IsDefined(sort))
        {
            throw new ArgumentOutOfRangeException(nameof(sort), sort, "定義されていない並べ方です。");
        }

        var today = JstClock.ToJstDate(now);
        var spellings = wordSet.Words.ToDictionary(w => w.Id, w => w.Word, StringComparer.Ordinal);
        var weak = progress.Words.Values.Where(w => w.State == LearningState.Weak).ToList();
        var mastered = weakOnly ? [] : progress.Words.Values.Where(w => w.State == LearningState.Mastered).ToList();

        // 並べる前の基準(最後の誤答の時点)は表示用の行とは別に持つ(極端な日時は表示では null になるため)
        IEnumerable<(WordProgress Word, LatestMistake? Last)> ordered = sort switch
        {
            WeakListSort.WeakFirst =>
                WeakWordScorer.Order(weak, settings, now)
                    .Concat(mastered
                        .OrderBy(w => w.NextReviewDate is null ? 1 : 0)
                        .ThenBy(w => w.NextReviewDate)
                        .ThenBy(w => w.WordId, StringComparer.Ordinal))
                    .Select(w => (w, LatestMistake.Of(w))),
            _ => weak.Concat(mastered)
                .Select(w => (Word: w, Last: LatestMistake.Of(w)))
                .OrderBy(x => x.Last is null ? 1 : 0)
                .ThenByDescending(x => x.Last?.At.UtcTicks)
                .ThenBy(x => x.Word.WordId, StringComparer.Ordinal),
        };

        return [.. ordered.Select(x => ToRow(x.Word, x.Last, spellings, settings, now, today))];
    }

    private static WeakWordRow ToRow(
        WordProgress w, LatestMistake? last, Dictionary<string, string> spellings, LearningSettings settings, DateTimeOffset now, DateOnly today)
    {
        var inSet = spellings.TryGetValue(w.WordId, out var spelling);
        var isWeak = w.State == LearningState.Weak;
        var required = settings.Mastery.RequiredCorrectDays;
        var typed = LatestMistake.TypedInputOf(w);
        return new WeakWordRow(
            w.WordId,
            inSet ? spelling! : w.WordId,
            inSet,
            w.State,
            w.NormalMistakes,
            new ModeAccuracy(
                w.Mode(LearningMode.Japanese).Accuracy,
                w.Mode(LearningMode.Audio).Accuracy,
                w.Mode(LearningMode.Blank).Accuracy,
                w.Mode(LearningMode.Flash).Accuracy),
            InputRules.TruncateForStorage(typed?.Input),
            last is null ? null : JstClock.TryToJst(last.At),
            isWeak ? w.MasteryStreak : null,
            isWeak ? required : null,
            // 連続正答が必要回数以上で苦手に残っている記録(必要回数を後から下げた)も、次の対象正答で克服するので最低1
            isWeak ? Math.Max(required - w.MasteryStreak, 1) : null,
            isWeak ? MasteryRules.CountedToday(w, today) : null,
            isWeak ? WeakWordScorer.Score(w, settings, now) : null,
            isWeak ? null : w.NextReviewDate,
            isWeak ? null : ReviewScheduler.IsDue(w, today))
        {
            LastWrongInputMode = typed?.Mode,
            LastWrongInputAt = typed is null ? null : JstClock.TryToJst(typed.At),
            Modes = [.. Enum.GetValues<LearningMode>().Select(mode => ToStat(mode, w.Mode(mode)))],
        };
    }

    private static ModeStat ToStat(LearningMode mode, ModeRecord r) =>
        new(mode, r.NormalCorrect, r.NormalWrong, r.RetryCorrect, r.RetryWrong, r.Accuracy);
}
