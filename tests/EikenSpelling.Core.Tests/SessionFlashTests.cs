using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Core.Tests;

/// <summary>フラッシュの表示開始の印(UI v0.3 フラッシュ途中再開 FA)。提示ごとに一意で、保存してから表示する前提。</summary>
public class SessionFlashTests
{
    private static Runner FlashAbc(int retry = 1) =>
        new(S.Start(S.Settings(retry, autoMix: false, manual: Flash), S.Q("a", Flash), S.Q("b", Flash), S.Q("c", Flash)));

    [Fact]
    public void New_flash_question_is_not_shown_until_marked()
    {
        var r = FlashAbc();

        Assert.False(SessionManager.IsFlashShown(r.Session));

        var marked = SessionManager.MarkFlashShown(r.Session);

        Assert.True(SessionManager.IsFlashShown(marked));
        Assert.Equal(0, marked.FlashShownStep);
        Assert.Empty(SessionValidator.Validate(marked, r.Progress));
    }

    [Fact]
    public void Mark_applies_only_to_current_presentation()
    {
        var r = FlashAbc();
        r.Mark();

        r.Answer(true);

        Assert.False(SessionManager.IsFlashShown(r.Session)); // 次の問題はまだ見せていない
        Assert.Empty(SessionValidator.Validate(r.Session, r.Progress));
    }

    [Fact]
    public void Deferred_presentation_of_same_question_needs_a_new_mark()
    {
        var r = FlashAbc();
        r.Mark();
        r.Skip(Jst.At(Jst.Day0, 9, 1)); // a を見た後に後回し
        r.Answer(true);
        r.Answer(true);

        Assert.Equal("a", SessionManager.Current(r.Session)!.WordId);
        Assert.False(SessionManager.IsFlashShown(r.Session)); // 後回しの a は別の提示
    }

    [Fact]
    public void Non_flash_question_cannot_be_marked()
    {
        var r = new Runner(S.Start(S.Settings(), S.Q("a", Japanese)));

        Assert.Throws<InvalidOperationException>(() => SessionManager.MarkFlashShown(r.Session));
        Assert.False(SessionManager.IsFlashShown(r.Session));
    }

    [Fact]
    public void Retry_flash_question_gets_its_own_mark()
    {
        var r = new Runner(S.Start(S.Settings(retry: 1, autoMix: false, manual: Flash), S.Q("a", Flash)));
        r.Mark();
        r.Answer(false); // 再練習が足される

        Assert.Equal(AttemptType.Retry, SessionManager.Current(r.Session)!.Attempt);
        Assert.False(SessionManager.IsFlashShown(r.Session));
        r.Mark();
        Assert.True(SessionManager.IsFlashShown(r.Session));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)] // まだ何も処理していないのに、次の提示の印
    public void Validator_rejects_mark_outside_processed_range(int step)
    {
        var bad = FlashAbc().Session with { FlashShownStep = step };

        Assert.NotEmpty(SessionValidator.Validate(bad, LearningProgress.Empty));
    }

    [Fact]
    public void Mark_round_trips_and_missing_field_reads_as_not_shown()
    {
        var r = FlashAbc();
        r.Mark();
        var json = ProgressJson.Serialize(new ProgressDocument { Progress = r.Progress, Session = r.Session });

        Assert.True(SessionManager.IsFlashShown(ProgressJson.Parse(json).Session!));

        var withoutField = System.Text.RegularExpressions.Regex.Replace(json, "\\s*\"flashShownStep\": 0,?", "");
        Assert.DoesNotContain("flashShownStep", withoutField, StringComparison.Ordinal);
        Assert.False(SessionManager.IsFlashShown(ProgressJson.Parse(withoutField).Session!));
    }
}
