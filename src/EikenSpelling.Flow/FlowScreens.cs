using System.Collections.Immutable;

using EikenSpelling.Core.Application;
using EikenSpelling.Core.Domain;
using EikenSpelling.Core.Models;

namespace EikenSpelling.Flow;

/// <summary>
/// 今の画面の状態(app-flow design 4.1)。変更不可。WPF はこれを表示し、操作を PracticeFlow に渡す。
/// 文言にはファイルのパス・誤答入力を入れない。画面の状態を丸ごとログに出さないこと(差分に入力の文字が入る)。
/// </summary>
public abstract record FlowScreen
{
    /// <summary>起動時の通知。起動・再読込の後の最初の画面だけが持つ。</summary>
    public ImmutableArray<string> Notices { get; init; } = [];
}

public sealed record HomeScreen(SelectionEmptyReason EmptyReason = SelectionEmptyReason.None, string? Message = null) : FlowScreen;

public sealed record ResumeSummary(DateTimeOffset StartedAt, int Answered, int Total);

/// <summary>未完了の練習がある。CanResume が偽なら「終了して新しく始める」だけ選べる。</summary>
public sealed record ResumePromptScreen(ResumeSummary Session, bool CanResume, string? CannotResumeReason, string? Message = null) : FlowScreen;

/// <summary>出題する問題。Japanese・Blanks は今の単語データから引く。</summary>
public sealed record QuestionView(string WordId, string Word, LearningMode Mode, string Japanese, string? Blanks, bool IsRetry);

/// <summary>Number は1始まり。Total は再練習が足されると増える。FlashSeconds はセッション開始時の設定の値。</summary>
public sealed record QuestionScreen(QuestionView Question, int Number, int Total, double FlashSeconds) : FlowScreen;

/// <summary>判定。Saved が偽の間は Next できない(再保存か終了だけ)。SessionId・SaveFailure はログ用。</summary>
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
