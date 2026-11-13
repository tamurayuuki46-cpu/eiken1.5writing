using System.Collections.Immutable;

using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

namespace EikenSpelling.Core.Application;

/// <summary>0語のときの理由(上から順に当てはめる)。</summary>
public enum SelectionEmptyReason
{
    None,
    NoWordsInSources,
    ManualModeUnavailable,
    NoEligibleWords,
    NoWeakWords,
    NothingToPractice,
}

/// <summary>選択の結果。件数は選んだ単語の種類ごと(Unseen は未出題、Seen は既出の通常単語)。</summary>
public sealed record SelectionResult(
    ImmutableArray<PlannedQuestion> Plan, int Unseen, int Seen, int Weak, int Review, SelectionEmptyReason EmptyReason);

/// <summary>
/// 出題する単語の選択(design.md 4章)。時刻と乱数は外から受け取る。
/// 単語データは WordSetValidator を通したものが前提だが、空の ID・綴りは除き、同じ ID は先にあるものだけ使う。
/// </summary>
public static class QuestionSelector
{
    public static SelectionResult Select(
        WordSet wordSet, LearningProgress progress, LearningSettings settings,
        IReadOnlySet<LearningMode> availableModes, DateTimeOffset now, Random random)
    {
        ArgumentNullException.ThrowIfNull(wordSet);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(availableModes);
        ArgumentNullException.ThrowIfNull(random);
        if (SettingsValidator.Validate(settings).Count > 0)
        {
            throw new ArgumentException("設定に不正な値があるため、出題を選べません。", nameof(settings));
        }

        // 今は TierThenFrequency だけ(SettingsValidator が定義外の値を弾く)。列挙に値を足したときの安全策
        if (settings.Vocabulary.Ordering != NewWordOrdering.TierThenFrequency)
        {
            throw new ArgumentException("新規単語の並べ方が対応外です。", nameof(settings));
        }

        var practice = settings.Practice;
        var today = JstClock.ToJstDate(now);
        var enabled = settings.Vocabulary.EnabledSources.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 単語データの順に固定して扱う(学習記録の辞書の列挙順は使わない)
        var words = CleanWords(wordSet);
        var candidates = new List<Candidate>();
        foreach (var word in words)
        {
            var modes = ModeEligibility.For(word, availableModes);
            if (modes.IsEmpty || (!practice.AutoMix && !modes.Contains(practice.ManualMode)))
            {
                continue;
            }

            candidates.Add(new Candidate(word, modes, progress.Word(word.Id), word.Sources.Any(enabled.Contains)));
        }

        // 再確認の並びは ReviewScheduler の期限一覧に任せる(予定日の古い順・同じ日は単語ID順)
        var byId = candidates.ToDictionary(c => c.Word.Id, StringComparer.Ordinal);
        // 見出しと WordId が食い違う記録(読込では弾かれる)が直接渡されても、重複や例外を起こさない
        var review = ReviewScheduler.DueWordIds(progress, today)
            .Where(id => id is not null && byId.TryGetValue(id, out var c) && c.Record is { } r
                && string.Equals(r.WordId, id, StringComparison.Ordinal) && ReviewScheduler.IsDue(r, today))
            .Distinct(StringComparer.Ordinal)
            .Select(id => byId[id])
            .ToList();

        // 苦手度の順に並べた記録から、同じ記録を持つ候補へ戻す(ID ではなく記録そのもので対応させる)
        var weakByRecord = new Dictionary<WordProgress, Candidate>(ReferenceEqualityComparer.Instance);
        foreach (var c in candidates.Where(c => c.Record?.State == LearningState.Weak))
        {
            weakByRecord[c.Record!] = c;
        }

        var weak = WeakWordScorer.Order(weakByRecord.Keys, settings, now).Select(r => weakByRecord[r]).ToList();

        // 新規の枠に入る単語だけ語彙リストで絞る(苦手・再確認は管理を続ける。v1.0 9.1)
        var tieKeys = candidates.ToDictionary(c => c.Word.Id, _ => random.NextDouble(), StringComparer.Ordinal);
        var unseen = candidates
            .Where(c => c.Record is null && c.InSources)
            .OrderBy(c => c.Word.PriorityTier)
            .ThenBy(c => c.Word.FrequencyRank.HasValue ? 0 : 1)
            .ThenBy(c => c.Word.FrequencyRank)
            .ThenBy(c => tieKeys[c.Word.Id])
            .ToList();
        var seen = candidates
            .Where(c => c.Record?.State == LearningState.Normal && c.InSources)
            .OrderBy(c => c.Record!.LastNormalAskedAt.HasValue ? 1 : 0)
            .ThenBy(c => c.Record!.LastNormalAskedAt)
            .ThenBy(c => c.Word.Id, StringComparer.Ordinal)
            .ToList();

        var slots = QuestionPlanner.PlanSlots(practice.QuestionCount, practice, settings.Mastery);
        var pickedReview = review.Take(slots.ReviewWords).ToList();
        var weakCap = slots.WeakWords + (slots.ReviewWords - pickedReview.Count);
        var pickedWeak = weak.Take(weakCap).ToList();
        var newCap = practice.WeakWordsOnly ? 0 : slots.NewWords + (weakCap - pickedWeak.Count);
        var pickedUnseen = unseen.Take(newCap).ToList();
        var pickedSeen = seen.Take(newCap - pickedUnseen.Count).ToList();
        var extraWeak = weak.Skip(pickedWeak.Count).Take(newCap - pickedUnseen.Count - pickedSeen.Count).ToList();
        pickedWeak.AddRange(extraWeak);

        var picked = pickedReview.Concat(pickedWeak).Concat(pickedUnseen).Concat(pickedSeen).ToList();
        if (picked.Count == 0)
        {
            return new SelectionResult([], 0, 0, 0, 0, EmptyReason(words, enabled, progress, today, practice, availableModes, candidates));
        }

        var plan = AssignModes(picked, practice, availableModes);
        Shuffle(plan, random);
        return new SelectionResult([.. plan], pickedUnseen.Count, pickedSeen.Count, pickedWeak.Count, pickedReview.Count, SelectionEmptyReason.None);
    }

    private static List<WordEntry> CleanWords(WordSet wordSet)
    {
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        return wordSet.Words
            .Where(w => w is not null && !string.IsNullOrWhiteSpace(w.Id) && !string.IsNullOrWhiteSpace(w.Word)
                && w.Sources is not null && seenIds.Add(w.Id))
            .ToList();
    }

    /// <summary>
    /// 自動ミックス: AllocateModes(実際に選んだ数)を目標に、使えるモードの少ない単語から(同数は単語ID順)、
    /// 残り目標の最も多いモード(同数は優先順)を割り当てる。使える中に残りが無ければ、重みのあるモードを優先順で。
    /// </summary>
    private static List<PlannedQuestion> AssignModes(List<Candidate> picked, PracticeSettings practice, IReadOnlySet<LearningMode> availableModes)
    {
        if (!practice.AutoMix)
        {
            return picked.Select(c => new PlannedQuestion(c.Word.Id, c.Word.Word, practice.ManualMode, c.Modes)).ToList();
        }

        var remaining = QuestionPlanner.AllocateModes(picked.Count, practice.ModeWeights, availableModes).ToDictionary(kv => kv.Key, kv => kv.Value);
        var assigned = new Dictionary<string, LearningMode>(StringComparer.Ordinal);
        foreach (var c in picked.OrderBy(c => c.Modes.Length).ThenBy(c => c.Word.Id, StringComparer.Ordinal))
        {
            var withRoom = c.Modes.Where(m => remaining[m] > 0).ToList();
            LearningMode mode;
            if (withRoom.Count > 0)
            {
                // 同数は LearningMode の並び(日本語 → 音声 → 穴あき → フラッシュ)= 優先順
                mode = withRoom.OrderByDescending(m => remaining[m]).ThenBy(m => (int)m).First();
                remaining[mode]--;
            }
            else
            {
                mode = c.Modes.OrderBy(m => practice.ModeWeights[m] > 0 ? 0 : 1).ThenBy(m => (int)m).First();
            }

            assigned[c.Word.Id] = mode;
        }

        return picked.Select(c => new PlannedQuestion(c.Word.Id, c.Word.Word, assigned[c.Word.Id], c.Modes)).ToList();
    }

    private static void Shuffle(List<PlannedQuestion> plan, Random random)
    {
        for (var i = plan.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (plan[i], plan[j]) = (plan[j], plan[i]);
        }
    }

    private static SelectionEmptyReason EmptyReason(
        List<WordEntry> words, HashSet<string> enabled, LearningProgress progress, DateOnly today,
        PracticeSettings practice, IReadOnlySet<LearningMode> availableModes, List<Candidate> candidates)
    {
        var hasManaged = words.Any(w => progress.Word(w.Id) is { } r
            && (r.State == LearningState.Weak || ReviewScheduler.IsDue(r, today)));
        if (!words.Any(w => w.Sources.Any(enabled.Contains)) && !hasManaged)
        {
            return SelectionEmptyReason.NoWordsInSources;
        }

        if (!practice.AutoMix && !availableModes.Contains(practice.ManualMode))
        {
            return SelectionEmptyReason.ManualModeUnavailable;
        }

        // 出せる単語の判定は、出題の対象になりうる範囲(語彙リスト内・苦手・期限が来た再確認)だけで行う
        if (!candidates.Any(c => c.InSources || c.Record?.State == LearningState.Weak || (c.Record is { } r && ReviewScheduler.IsDue(r, today))))
        {
            return SelectionEmptyReason.NoEligibleWords;
        }

        return practice.WeakWordsOnly ? SelectionEmptyReason.NoWeakWords : SelectionEmptyReason.NothingToPractice;
    }

    private sealed record Candidate(WordEntry Word, ImmutableArray<LearningMode> Modes, WordProgress? Record, bool InSources);
}
