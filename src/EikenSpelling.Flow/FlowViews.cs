using EikenSpelling.Core.Application;
using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

namespace EikenSpelling.Flow;

/// <summary>今の記録からホーム(学習状況つき)と苦手一覧の画面を作る(weak-list design 4.3)。何も書かない。</summary>
internal static class FlowViews
{
    public static HomeScreen Home(
        WordSet? wordSet, LearningProgress progress, DateTimeOffset now,
        SelectionEmptyReason emptyReason = SelectionEmptyReason.None, string? message = null)
    {
        var status = wordSet is null ? StudyStatus.Empty : StudyStatus.From(wordSet, progress, JstClock.ToJstDate(now));
        return new HomeScreen(status, emptyReason, message);
    }

    public static WeakListScreen WeakList(
        WordSet wordSet, LearningProgress progress, LearningSettings settings, DateTimeOffset now, WeakListSort sort, bool weakOnly) =>
        new(WeakWordList.Build(progress, wordSet, settings, now, sort, weakOnly), sort, weakOnly);
}
