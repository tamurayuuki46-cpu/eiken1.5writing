using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

namespace EikenSpelling.Core.Tests;

/// <summary>学習記録のテスト用: 回答を順に Apply し、記録を持ち回る。</summary>
internal sealed class Learner
{
    private static readonly Guid FixedSession = new("11111111-1111-1111-1111-111111111111");

    private int _index;

    public LearningProgress Progress { get; set; } = LearningProgress.Empty;

    public LearningSettings Settings { get; set; } = DefaultSettings.Create();

    public Guid Session { get; set; } = FixedSession;

    public AnswerResult Answer(
        string word, LearningMode mode, bool correct, DateTimeOffset at,
        AttemptType type = AttemptType.Normal, string? input = null)
    {
        var answer = new AnswerEvent(
            new AnswerId(Session, _index++, type), word, mode, correct, input ?? (correct ? word : word + "x"), at);
        return Submit(answer);
    }

    public AnswerResult Submit(AnswerEvent answer)
    {
        var result = AnswerRecorder.Apply(Progress, answer, Settings);
        Progress = result.Progress;
        return result;
    }

    public WordProgress Word(string word) => Progress.Word(word) ?? throw new InvalidOperationException($"{word} の記録がありません。");

    /// <summary>Day0 に誤答して苦手にする。</summary>
    public void MakeWeak(string word, DateOnly day) => Answer(word, LearningMode.Japanese, false, Jst.At(day));

    /// <summary>苦手にしたあと、必要回数の日数だけ日本語で正答して克服させる。克服した日を返す。</summary>
    public DateOnly MakeMastered(string word, DateOnly day)
    {
        MakeWeak(word, day);
        var d = day;
        for (var i = 0; i < Settings.Mastery.RequiredCorrectDays; i++)
        {
            d = d.AddDays(1);
            Answer(word, LearningMode.Japanese, true, Jst.At(d));
        }

        return d;
    }
}

internal static class Jst
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(9);

    public static readonly DateOnly Day0 = new(2026, 11, 1);

    public static DateTimeOffset At(DateOnly day, int hour = 12, int minute = 0, int second = 0) =>
        new(day.ToDateTime(new TimeOnly(hour, minute, second)), Offset);
}
