using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;

namespace EikenSpelling.Core.Application;

/// <summary>
/// ホームの学習状況(weak-list design 4.1)。今の単語データの語を状態ごとに数える。
/// 単語データに無い記録は数えない。ReviewDue は克服済みのうち再確認の期限が来たもの(Mastered に含まれる)。
/// </summary>
public sealed record StudyStatus(int Unseen, int Normal, int Weak, int Mastered, int ReviewDue)
{
    /// <summary>単語データを読む前の仮の値。</summary>
    public static readonly StudyStatus Empty = new(0, 0, 0, 0, 0);

    /// <summary>単語データは WordSetValidator を通したもの(ID の重複なし)が前提。</summary>
    public static StudyStatus From(WordSet wordSet, LearningProgress progress, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(wordSet);
        ArgumentNullException.ThrowIfNull(progress);

        int unseen = 0, normal = 0, weak = 0, mastered = 0, due = 0;
        foreach (var entry in wordSet.Words)
        {
            switch (progress.Word(entry.Id))
            {
                case null:
                    unseen++;
                    break;
                case { State: LearningState.Weak }:
                    weak++;
                    break;
                case { State: LearningState.Mastered } record:
                    mastered++;
                    due += ReviewScheduler.IsDue(record, today) ? 1 : 0;
                    break;
                default:
                    normal++;
                    break;
            }
        }

        return new StudyStatus(unseen, normal, weak, mastered, due);
    }
}
