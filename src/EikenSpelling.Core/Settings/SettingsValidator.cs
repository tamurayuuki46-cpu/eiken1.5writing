using EikenSpelling.Core.Models;

namespace EikenSpelling.Core.Settings;

public sealed record SettingsIssue(string Path, string Message);

/// <summary>
/// 設定値の検証(v1.2 8章)。不正な設定は保存しない・使わないための判定で、学習記録には触れない。
/// </summary>
public static class SettingsValidator
{
    /// <param name="settings">検証する設定。</param>
    /// <param name="availableModes">この端末で使えるモード。null なら4モードすべて使える前提。</param>
    /// <param name="wordSet">単語データ。渡すと有効な語彙リストに単語があるかも確認する。</param>
    public static IReadOnlyList<SettingsIssue> Validate(
        LearningSettings settings,
        IReadOnlySet<LearningMode>? availableModes = null,
        WordSet? wordSet = null)
    {
        var issues = new List<SettingsIssue>();
        void Add(string path, string message) => issues.Add(new(path, message));

        if (settings.SettingsVersion > LearningSettings.CurrentVersion)
        {
            Add("settingsVersion", $"設定のバージョン{settings.SettingsVersion}はこのアプリでは読めません。");
        }

        var p = settings.Practice;
        if (p.QuestionCount <= 0)
        {
            Add("practice.questionCount", "問題数は1以上にしてください。");
        }

        if (p.QuestionCountChoices.Count == 0 || p.QuestionCountChoices.Any(c => c <= 0))
        {
            Add("practice.questionCountChoices", "問題数の選択肢は1以上の数を1つ以上指定してください。");
        }

        if (double.IsNaN(p.NewWordRatio) || p.NewWordRatio < 0 || p.NewWordRatio > 1)
        {
            Add("practice.newWordRatio", "新規単語の割合は0〜1にしてください。");
        }

        var modes = availableModes ?? Enum.GetValues<LearningMode>().ToHashSet();
        foreach (var mode in Enum.GetValues<LearningMode>())
        {
            if (p.ModeWeights[mode] < 0)
            {
                Add($"practice.modeWeights.{ToCamel(mode)}", "モードの重みは0以上にしてください。");
            }
        }

        if (p.AutoMix && modes.Sum(m => Math.Max(0, p.ModeWeights[m])) <= 0)
        {
            Add("practice.modeWeights", "使えるモードの重みの合計が0のため、自動ミックスで出題できません。");
        }

        if (!p.AutoMix && !modes.Contains(p.ManualMode))
        {
            Add("practice.manualMode", "選んだモードはこの環境では使えません。");
        }

        if (!(p.FlashSeconds > 0))
        {
            Add("practice.flashSeconds", "フラッシュの表示時間は0秒より長くしてください。");
        }

        if (p.FlashSecondsChoices.Count == 0 || p.FlashSecondsChoices.Any(s => !(s > 0)))
        {
            Add("practice.flashSecondsChoices", "フラッシュ表示時間の選択肢は0秒より長い値を1つ以上指定してください。");
        }

        if (p.RetryCount < 0)
        {
            Add("practice.retryCount", "再練習回数は0以上にしてください。");
        }

        var m = settings.Mastery;
        if (m.RequiredCorrectDays < 1)
        {
            Add("mastery.requiredCorrectDays", "克服に必要な正答日数は1以上にしてください。");
        }

        if (m.ReviewIntervalsDays.Count == 0 || m.ReviewIntervalsDays.Any(d => d <= 0))
        {
            Add("mastery.reviewIntervalsDays", "再確認間隔は1日以上の値を1つ以上指定してください。");
        }

        if (m.ReviewLimits.Count == 0)
        {
            Add("mastery.reviewLimits", "再確認枠の区切りを1つ以上指定してください。");
        }
        else
        {
            for (var i = 0; i < m.ReviewLimits.Count; i++)
            {
                var limit = m.ReviewLimits[i];
                if (limit.UpTo <= 0 || limit.Limit < 0)
                {
                    Add($"mastery.reviewLimits[{i}]", "再確認枠の区切りは問題数1以上、上限0以上にしてください。");
                }

                if (i > 0 && limit.UpTo <= m.ReviewLimits[i - 1].UpTo)
                {
                    Add($"mastery.reviewLimits[{i}]", "再確認枠の区切りは問題数の小さい順に並べてください。");
                }
            }
        }

        var w = settings.WeakWordScore;
        if (w.MarkWeakAfterMistakes < 1)
        {
            Add("weakWordScore.markWeakAfterMistakes", "苦手登録の誤答回数は1以上にしてください。");
        }

        if (w.MistakeCap < 1)
        {
            Add("weakWordScore.mistakeCap", "誤答数の上限は1以上にしてください。");
        }

        if (w.RecentMistakeDays < 0 || w.RecentMistakeBonus < 0 || w.PendingMasteryBonus < 0
            || w.OverdueDays < 0 || w.OverdueBonus < 0)
        {
            Add("weakWordScore", "苦手度の日数と加点は0以上にしてください。");
        }

        var v = settings.Vocabulary;
        if (v.EnabledSources.Count == 0)
        {
            Add("vocabulary.enabledSources", "使用する語彙リストを1つ以上選んでください。");
        }
        else if (wordSet is not null)
        {
            var enabled = v.EnabledSources.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!wordSet.Words.Any(word => word.Sources.Any(enabled.Contains)))
            {
                Add("vocabulary.enabledSources", "選んだ語彙リストに出題できる単語がありません。");
            }
        }

        return issues;
    }

    private static string ToCamel(LearningMode mode)
    {
        var name = mode.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
