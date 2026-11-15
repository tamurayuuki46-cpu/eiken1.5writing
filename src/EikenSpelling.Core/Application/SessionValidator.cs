using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

namespace EikenSpelling.Core.Application;

/// <summary>
/// 読み込んだセッションの検査(design.md 4章)。問題があれば ProgressRepository がセッションだけ捨てる。
/// 再練習の列は Replay で作り直して比べる(正解した単語の再練習・回数超過・順序・モードの違いをまとめて検出)。
/// 文言に入力やパスを含めない。
/// </summary>
public static class SessionValidator
{
    public static IReadOnlyList<string> Validate(PracticeSession session, LearningProgress progress)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(progress);
        var problems = new List<string>();

        if (session.SessionId == Guid.Empty)
        {
            problems.Add("セッションIDが空です。");
        }

        if (session.Settings is null || LearningSettingsJson.HasNullElements(session.Settings)
            || SettingsValidator.Validate(session.Settings).Count > 0)
        {
            problems.Add("開始時の設定が不正です。");
            return problems;
        }

        if (session.Questions.IsDefault || session.Answers.IsDefault || session.Skips.IsDefault
            || session.Questions.Any(q => q is null) || session.Answers.Any(a => a is null) || session.Skips.Any(s => s is null))
        {
            problems.Add("問題または回答に空(null)の要素があります。");
            return problems;
        }

        var normals = session.Questions.TakeWhile(q => q.Attempt == AttemptType.Normal).ToList();
        if (normals.Count == 0 || normals.Count > SettingsLimits.MaxQuestionCount)
        {
            problems.Add($"通常問題が1〜{SettingsLimits.MaxQuestionCount}件ではありません。");
            return problems;
        }

        if (session.Questions.Skip(normals.Count).Any(q => q.Attempt != AttemptType.Retry))
        {
            problems.Add("再練習の後に通常問題があります。");
        }

        foreach (var q in session.Questions)
        {
            if (!Enum.IsDefined(q.Attempt)
                || SessionManager.CheckQuestion(q.WordId, q.Word, q.Mode, q.EligibleModes) is not null)
            {
                problems.Add("問題の内容が不正です。");
                break;
            }
        }

        if (normals.Select(q => q.WordId).Distinct(StringComparer.Ordinal).Count() != normals.Count)
        {
            problems.Add("同じ単語が通常問題に2回あります。");
        }

        if (!session.Settings.Practice.AutoMix && normals.Any(q => q.Mode != session.Settings.Practice.ManualMode))
        {
            problems.Add("手動モードの問題のモードが設定と違います。");
        }

        if (session.Answers.Any(a => (a.StateBefore is { } b && !Enum.IsDefined(b)) || (a.StateAfter is { } x && !Enum.IsDefined(x))))
        {
            problems.Add("回答の状態の値が定義外です。");
        }

        if (session.FlashShownStep is { } shown && (shown < 0 || shown > session.ProcessedSteps))
        {
            problems.Add("フラッシュの表示開始の印が範囲外です。");
        }

        if (CheckSkips(session, normals.Count) is { } skipProblem)
        {
            problems.Add(skipProblem);
            return problems;
        }

        // このセッションの反映済み answerId は、採点済みの枠(採点の順の先頭 Answers.Length 件)とちょうど一致する
        // (ずれると再開後に進めない・記録が飛ぶ。スキップでは増えない)
        var order = session.AnswerOrder;
        var expectedIds = order.Take(Math.Min(session.Answers.Length, session.Questions.Length))
            .Select(i => new AnswerId(session.SessionId, i, session.Questions[i].Attempt))
            .ToHashSet();
        if (!progress.AppliedAnswerIds.Where(id => id.SessionId == session.SessionId).ToHashSet().SetEquals(expectedIds))
        {
            problems.Add("反映済みの回答IDが、回答済みの問題と一致しません。");
        }

        if (session.Answers.Length >= session.Questions.Length)
        {
            problems.Add("回答が問題の数以上あります(完了したセッションは保存しない)。");
        }

        if (problems.Count == 0)
        {
            var expected = SessionManager.Replay(normals, session.Skips, session.Answers, session.Settings);
            if (!expected.SequenceEqual(session.Questions, SessionQuestionComparer.Instance))
            {
                problems.Add("再練習の列が回答の結果と一致しません。");
            }
        }

        if (session.Questions.Where(q => q.Attempt == AttemptType.Retry).Any(q => progress.Word(q.WordId) is null))
        {
            problems.Add("再練習の単語が学習記録にありません。");
        }

        return problems;
    }

    /// <summary>
    /// スキップの台帳の検査: 通常問題の番号・重複なし・スキップした順(= 番号の昇順)・
    /// スキップの時点でそれより前の通常問題をすべて処理済み(スキップしなかった前の通常問題は採点済み)。
    /// </summary>
    private static string? CheckSkips(PracticeSession session, int normalCount)
    {
        if (session.Skips.IsEmpty)
        {
            return null;
        }

        var indexes = session.Skips.Select(s => s.QuestionIndex).ToList();
        if (indexes.Any(i => i < 0 || i >= normalCount))
        {
            return "スキップした問題の番号が通常問題の範囲外です。";
        }

        for (var k = 1; k < indexes.Count; k++)
        {
            if (indexes[k] <= indexes[k - 1])
            {
                return "スキップした問題が重複しているか、順序が正しくありません。";
            }
        }

        var lastSkipped = indexes[^1];
        var answeredBefore = Enumerable.Range(0, lastSkipped).Count(i => !indexes.Contains(i));
        return session.Answers.Length < answeredBefore
            ? "スキップより前の通常問題が採点されていません。"
            : null;
    }

    private sealed class SessionQuestionComparer : IEqualityComparer<SessionQuestion>
    {
        public static readonly SessionQuestionComparer Instance = new();

        public bool Equals(SessionQuestion? x, SessionQuestion? y) =>
            x is not null && y is not null
            && string.Equals(x.WordId, y.WordId, StringComparison.Ordinal)
            && string.Equals(x.Word, y.Word, StringComparison.Ordinal)
            && x.Mode == y.Mode && x.Attempt == y.Attempt
            && x.EligibleModes.SequenceEqual(y.EligibleModes);

        public int GetHashCode(SessionQuestion obj) => StringComparer.Ordinal.GetHashCode(obj.WordId);
    }
}
