using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;

namespace EikenSpelling.Presentation.Text;

/// <summary>
/// 値の言い換えだけを持つ文言表(design 4.5)。判断は Flow・Core が済ませた値を受け取る。
/// 判断を伴う文言(保存失敗・通知・再開できない理由)は Flow、差分の文章は Core の SpellingDiffService.Describe。
/// </summary>
public static class ScreenText
{
    public static string ModeName(LearningMode mode) => mode switch
    {
        LearningMode.Japanese => "日本語",
        LearningMode.Audio => "音声",
        LearningMode.Blank => "穴あき",
        LearningMode.Flash => "フラッシュ",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    public const string AutoMix = "自動ミックス";

    /// <summary>端末で使えない手動モード(保存済みの値を出すときだけ)。</summary>
    public static string UnavailableMode(LearningMode mode) => $"{ModeName(mode)}(この端末では使えません)";

    /// <summary>0語の理由。None は null。</summary>
    public static string? EmptyReason(SelectionEmptyReason reason) => reason switch
    {
        SelectionEmptyReason.None => null,
        SelectionEmptyReason.NoWordsInSources => "選んでいる語彙リストに単語がありません。",
        SelectionEmptyReason.ManualModeUnavailable => "選んだ出題方法は、この端末では使えません。ほかの出題方法を選んでください。",
        SelectionEmptyReason.NoEligibleWords => "選んだ出題方法で出せる単語がありません。",
        SelectionEmptyReason.NoWeakWords => "苦手な単語がまだありません。「苦手のみ」を外して練習してください。",
        SelectionEmptyReason.NothingToPractice => "今は練習できる単語がありません。",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    /// <summary>判定画面の「記録の変化」。変化が無ければ空。</summary>
    public static IReadOnlyList<string> Changes(AnswerChange change)
    {
        var lines = new List<string>();
        if (change.BecameMastered)
        {
            lines.Add("克服しました");
        }
        else if (change.ReturnedToWeak)
        {
            lines.Add("苦手に戻りました");
        }
        else if (change.StateAfter == LearningState.Weak && change.StateBefore != LearningState.Weak)
        {
            lines.Add("苦手に登録されました");
        }

        if (change.ReviewAdvanced)
        {
            lines.Add("再確認が進みました");
        }

        return lines;
    }

    /// <summary>結果画面の「苦手状態の変化」の見出し。</summary>
    public static string StateChange(StateChangeKind kind) => kind switch
    {
        StateChangeKind.BecameWeak => "苦手に登録",
        StateChangeKind.Mastered => "克服",
        StateChangeKind.ReturnedToWeak => "苦手に戻った",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
