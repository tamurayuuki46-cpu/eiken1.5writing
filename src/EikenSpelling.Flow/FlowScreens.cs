using System.Collections.Immutable;

using EikenSpelling.Core.Application;
using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;
using EikenSpelling.Core.Settings;

namespace EikenSpelling.Flow;

/// <summary>
/// 今の画面の状態(app-flow design 4.1)。変更不可。WPF はこれを表示し、操作を PracticeFlow に渡す。
/// 文言にはファイルのパス・誤答入力を入れない。画面の状態を丸ごとログに出さないこと(差分に入力の文字が入る)。
/// </summary>
public abstract record FlowScreen
{
    /// <summary>起動時の通知。起動・再読込の後の最初の画面だけが持つ。</summary>
    public ImmutableArray<string> Notices { get; init; } = [];

    /// <summary>
    /// ログ用の理由(Persistence の Reason など。固定文でパス・入力を含まない)。画面には出さない。
    /// 保存・読込に失敗した画面だけが持つ(WPF はこれと SessionId を error でログに出す。logging.md)。
    /// </summary>
    public string? Diagnostic { get; init; }
}

/// <summary>ホーム。Status は今の学習記録から作った学習状況(単語データを読む前は StudyStatus.Empty)。</summary>
public sealed record HomeScreen(
    StudyStatus Status, SelectionEmptyReason EmptyReason = SelectionEmptyReason.None, string? Message = null) : FlowScreen
{
    /// <summary>練習設定の初期値(保存済みの基本設定)。WPF の練習設定画面はこれを最初に選んでおく。</summary>
    public PracticeChoice Defaults { get; init; } = PracticeChoiceApplier.Defaults;
}

/// <summary>
/// 苦手一覧(v1.0 11.4)。Rows の LastWrongInput は利用者が打った文字列(表示用)。画面の状態をログに出さないこと。
/// EmptyReason・Message・Diagnostic は直前の開始が0語・保存失敗だったときだけ持つ。
/// </summary>
public sealed record WeakListScreen(
    ImmutableArray<WeakWordRow> Rows,
    WeakListSort Sort,
    bool WeakOnly,
    SelectionEmptyReason EmptyReason = SelectionEmptyReason.None,
    string? Message = null) : FlowScreen
{
    /// <summary>練習設定の初期値(保存済みの基本設定)。一覧から始める練習の苦手のみは常に真になる。</summary>
    public PracticeChoice Defaults { get; init; } = PracticeChoiceApplier.Defaults;
}

/// <summary>
/// 基本設定の画面(v1.2 2章 A)。Values は画面の値(選択肢・ManualModes に無い値のこともある。WPF はその値も表示する)。
/// CanSave が偽なら保存できない(理由は CannotSaveReason)。Note は保存の前に知らせる注意、Issues は検査で見つかった項目。
/// </summary>
public sealed record SettingsScreen(
    PracticeChoice Values,
    ImmutableArray<int> QuestionCountChoices,
    ImmutableArray<double> FlashSecondsChoices,
    ImmutableArray<LearningMode> ManualModes,
    bool CanSave,
    string? CannotSaveReason,
    string? Note,
    string? Message = null) : FlowScreen
{
    public ImmutableArray<SettingsIssue> Issues { get; init; } = [];
}

public sealed record ResumeSummary(DateTimeOffset StartedAt, int Answered, int Total);

/// <summary>未完了の練習がある。CanResume が偽なら「終了して新しく始める」だけ選べる。</summary>
public sealed record ResumePromptScreen(ResumeSummary Session, bool CanResume, string? CannotResumeReason, string? Message = null) : FlowScreen;

/// <summary>出題する問題。Japanese・Blanks は今の単語データから引く。</summary>
public sealed record QuestionView(string WordId, string Word, LearningMode Mode, string Japanese, string? Blanks, bool IsRetry);

/// <summary>Number は1始まり。Total は再練習が足されると増える。FlashSeconds はセッション開始時の設定の値。</summary>
public sealed record QuestionScreen(QuestionView Question, int Number, int Total, double FlashSeconds) : FlowScreen;

/// <summary>判定。Saved が偽の間は Next できない(再保存か終了だけ)。SaveFailure は画面の文言、原因は Diagnostic。SessionId はログ用。</summary>
public sealed record FeedbackScreen(
    bool IsCorrect,
    string CorrectWord,
    ImmutableArray<SpellingDiff> Diff,
    AnswerChange Change,
    bool Completed,
    bool Saved,
    string? SaveFailure,
    Guid SessionId) : FlowScreen;

public sealed record ResultScreen(SessionSummary Summary) : FlowScreen;

public sealed record BothCorruptScreen(string? Message = null) : FlowScreen;

public enum FatalKind
{
    FolderInvalid,
    WordSetInvalid,
    NewerVersion,
    Unavailable,
}

/// <summary>保存も練習もできない。Unavailable だけ再読込できる。</summary>
public sealed record FatalScreen(FatalKind Kind, string Reason) : FlowScreen
{
    public bool CanReload => Kind == FatalKind.Unavailable;
}
