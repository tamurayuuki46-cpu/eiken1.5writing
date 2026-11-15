using EikenSpelling.Core.Models;

namespace EikenSpelling.Core.Application;

/// <summary>
/// 今日の目標と実績(ロードマップ④、UI v0.3 S-10)。Graded が null なら「記録なし」(0問とは区別する)。
/// 実績は学習記録だけから、目標は設定から求める(目標を変えても実績は変わらない)。
/// </summary>
public sealed record DailyProgress(DateOnly Today, int? Graded, int Goal)
{
    public bool NoRecord => Graded is null;

    public bool Achieved => Graded >= Goal;

    /// <summary>目標まであと何問(達成・記録なしは 0)。</summary>
    public int Remaining => Graded is { } g ? Math.Max(Goal - g, 0) : 0;

    /// <param name="today">端末のローカル日付(呼び出し側が TimeProvider から作る)。</param>
    public static DailyProgress From(LearningProgress progress, DateOnly today, int goal)
    {
        ArgumentNullException.ThrowIfNull(progress);

        // 記録を始めていない: 学習記録が空なら何も起きていないことが確か(0問)。記録があれば旧データなので推測しない
        int? graded = progress.Daily.RecordingSince is null && progress.Words.IsEmpty
            ? 0
            : progress.Daily.CountOn(today);
        return new DailyProgress(today, graded, goal);
    }
}
