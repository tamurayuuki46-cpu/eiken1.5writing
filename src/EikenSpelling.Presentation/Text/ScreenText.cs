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

    /// <summary>音声モードの案内(綴り・文字数は出さない。音声モード A4・Q-A3)。</summary>
    public const string AudioPrompt = "音声を聞いて、英単語を入力してください";

    /// <summary>フラッシュモードの案内。</summary>
    public const string FlashPrompt = "表示された英単語を覚えて、消えたら入力してください";

    /// <summary>読み上げに失敗したとき(音声モード A6)。</summary>
    public const string SpeechFailed = "音声を再生できませんでした。もう一度聞くか、やめて別の出題方法で練習してください。";

    /// <summary>端末に英語の音声が無いとき(音声モード A7、UI v0.3 UC-31)。</summary>
    public const string AudioUnavailable = "英語の音声が入っていないため、音声モードは使えません。";

    /// <summary>フラッシュの表示秒数の表示(0.5 → 「0.5秒」)。</summary>
    public static string Seconds(double seconds) => seconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "秒";

    /// <summary>
    /// 段階別の進み具合の1行(UI v0.3 FA)。通常・後回しは「通常:全5問中の2問目」、再練習は「再練習2問目・残り1問」
    /// (total は再練習では今の問題を含む残り)。正解の文字を含まないのでスクリーンリーダー用の名前にも使える。
    /// </summary>
    public static string ProgressText(QuestionPhase phase, int number, int total) => phase switch
    {
        QuestionPhase.Retry => $"再練習{number}問目・残り{total}問",
        _ => $"{PhaseName(phase)}:全{total}問中の{number}問目",
    };

    /// <summary>出題の段階の名前(UI v0.3 段階別の進捗表示)。</summary>
    public static string PhaseName(QuestionPhase phase) => phase switch
    {
        QuestionPhase.Normal => "通常",
        QuestionPhase.Deferred => "後回し",
        QuestionPhase.Retry => "再練習",
        _ => throw new ArgumentOutOfRangeException(nameof(phase)),
    };

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
        SelectionEmptyReason.EmptyScope => "この語彙セットには、出題できる単語がありません。",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    // ---- 語彙セット(UI v0.3 S-06) ----

    public static string StudyState(WordStudyState state) => state switch
    {
        WordStudyState.Unseen => "未出題",
        WordStudyState.Normal => "通常",
        WordStudyState.Weak => "苦手",
        WordStudyState.Mastered => "克服済み",
        WordStudyState.ReviewDue => "再確認待ち",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    /// <summary>「セット 2 / 99(51〜100語目・全4,924語)」。</summary>
    public static string SetPageText(int unitSize, int number, int total, int totalWords)
    {
        if (total == 0)
        {
            return "語彙セットはありません";
        }

        var first = ((number - 1) * unitSize) + 1;
        var last = Math.Min(number * unitSize, totalWords);
        return $"{SetNumberText(number, total)}({first}〜{last}語目・全{totalWords}語)";
    }

    public static string SetNumberText(int number, int total) => $"セット {number} / {total}";

    public static string SetUnitLabel(int unitSize, bool selected) => selected ? $"{unitSize}語ずつ(選択中)" : $"{unitSize}語ずつ";

    public static string SetShortHeadline(int available, int desired) => $"このセットでは{available}問出題できます(希望{desired}問)";

    public static string SetFullHeadline(int available) => $"このセットから{available}問を出題します";

    public const string SetNothingHeadline = "このセットでは出題できる問題がありません";

    public static string SetStartLabel(int available) => $"{available}問で開始";

    public const string SetRetryStartLabel = "もう一度保存して開始";

    public const string SetPlanChanged = "条件が変わったため、出題できる問題を確かめ直しました。もう一度確認してください。";

    public const string SetBackToConditions = "条件に戻る";

    public const string SetNothingHint = "問題数・出題方法・「苦手のみ」を変えるか、別のセットを選んでください。";

    public static string ResumeSetText(int unitSize, int number, int total) => $"語彙セット {number} / {total}({unitSize}語ずつ)から始めた練習";

    public const string ResumeSetUpdated = "単語データが更新されたため、今のセットの区切りとは違う場合があります。この練習は始めたときの語のまま続けられます。";

    public static string ResumePhaseText(QuestionPhase phase, int skipped) => phase switch
    {
        QuestionPhase.Normal when skipped > 0 => $"次は通常問題です(後回し {skipped}問)",
        QuestionPhase.Normal => "次は通常問題です",
        QuestionPhase.Deferred => $"次は後回しの問題です(後回し {skipped}問)",
        _ => "次は再練習です",
    };

    /// <summary>後回しで「答えを見る」を選んだ判定の見出し(UI v0.3 S-04)。</summary>
    public const string RevealedResult = "不正解(答えを表示)";

    /// <summary>結果画面の間違えた単語に添える印。</summary>
    public const string RevealedMark = "(答えを見た)";

    public const string RevealLabel = "答えを見る";

    public const string RevealHelp = "答えを表示します。この問題は不正解として記録されます";

    public const string BackToResult = "結果へ戻る";

    public const string BackToHome = "ホームへ戻る";

    public const string NoTypedInput = "記録なし";

    public const string NoWeakWordsInList = "苦手な単語はまだありません。";

    public const string NoReviewTargets = "今は復習する単語がありません(苦手な単語も、再確認の日が来た単語もありません)。";

    public const string OpenDetail = "詳細";

    public const string CloseDetail = "閉じる";

    /// <summary>苦手一覧の行の状態。</summary>
    public static string WeakRowState(LearningState state) => state == LearningState.Weak ? "苦手" : "克服済み";

    public static string NormalMistakes(int count) => $"通常の誤答 {count}回";

    /// <summary>日時の表示(日本時間に直した値)。無ければ「記録なし」。</summary>
    public static string DateTimeOrNone(DateTimeOffset? at) => at is { } t ? DateTime(t) : NoTypedInput;

    public static string DateTime(DateTimeOffset at) => at.ToString("M月d日 HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>実入力のモードと日時(入力の文字は含めない)。</summary>
    public static string TypedInputMeta(LearningMode mode, DateTimeOffset? at) =>
        at is { } t ? $"({ModeName(mode)}・{DateTime(t)})" : $"({ModeName(mode)})";

    /// <summary>苦手一覧の並べ方のボタン(選んでいる方に印)。</summary>
    public static string SortLabel(WeakListSort sort, bool selected)
    {
        var name = sort == WeakListSort.WeakFirst ? "苦手度の順" : "新しい誤答の順";
        return selected ? $"{name}(選択中)" : name;
    }

    public static string WeakOnlyToggle(bool weakOnly) => weakOnly ? "すべて表示する" : "苦手だけ表示する";

    /// <summary>苦手一覧から始めた一括復習が0語だった理由(この画面では「苦手のみ」を外せないので、ホームの文言と分ける)。</summary>
    public static string? WeakListEmptyReason(SelectionEmptyReason reason) => reason switch
    {
        SelectionEmptyReason.NoWeakWords => NoReviewTargets,
        _ => EmptyReason(reason),
    };

    /// <summary>モード別の1行(通常と再練習を分ける。正答率は通常問題だけ)。</summary>
    public static string ModeLine(ModeStat s) => s.Accuracy is { } a
        ? $"{ModeName(s.Mode)}: 正答率 {Math.Round(a * 100)}%(通常 正解{s.NormalCorrect}・誤答{s.NormalWrong} / 再練習 正解{s.RetryCorrect}・誤答{s.RetryWrong})"
        : $"{ModeName(s.Mode)}: 未学習(再練習 正解{s.RetryCorrect}・誤答{s.RetryWrong})";

    /// <summary>克服・再確認の情報(苦手は克服までの残り、克服済みは再確認日)。</summary>
    public static string WeakStatus(WeakWordRow row)
    {
        if (row.State == LearningState.Weak)
        {
            var streak = row.MasteryStreak ?? 0;
            var required = row.RequiredCorrectDays ?? 0;
            var today = row.CountedToday == true ? "・今日は対象の正解済み" : "";
            return $"苦手: 克服まであと{row.RemainingForMastery}日分(連続正答 {streak}/{required}{today})";
        }

        var next = row.NextReviewDate is { } d ? $"{d:M月d日}" : "予定なし";
        return row.ReviewDue == true ? $"克服済み: 再確認待ち(予定日 {next})" : $"克服済み: 次の再確認 {next}";
    }

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
