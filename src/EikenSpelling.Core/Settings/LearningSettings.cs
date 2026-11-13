using System.Text.Json.Serialization;
using EikenSpelling.Core.Models;

namespace EikenSpelling.Core.Settings;

/// <summary>
/// 学習設定(v1.2)。値は学習ロジックから分離し、Core は受け取った設定で動く。
/// 各プロパティの初期値が v1.1 のデフォルトそのもので、設定ファイルに無い項目もこの値で補われる。
/// </summary>
public sealed record LearningSettings
{
    public const int CurrentVersion = 1;

    public int SettingsVersion { get; init; } = CurrentVersion;

    public PracticeSettings Practice { get; init; } = new();

    public MasterySettings Mastery { get; init; } = new();

    public WeakWordScoreSettings WeakWordScore { get; init; } = new();

    public VocabularySettings Vocabulary { get; init; } = new();
}

/// <summary>練習1回分の設定(基本設定+詳細設定)。</summary>
public sealed record PracticeSettings
{
    /// <summary>通常問題で出題する異なる単語数(Q6)。</summary>
    public int QuestionCount { get; init; } = 10;

    /// <summary>基本設定UIで選べる問題数。</summary>
    public IReadOnlyList<int> QuestionCountChoices { get; init; } = [5, 10, 20];

    /// <summary>true なら自動ミックス、false なら ManualMode の1モードだけ(Q2)。</summary>
    public bool AutoMix { get; init; } = true;

    public LearningMode ManualMode { get; init; } = LearningMode.Japanese;

    public bool WeakWordsOnly { get; init; }

    /// <summary>新規単語の割合(Q11)。端数は新規側に切り上げる(5問なら新規3・苦手2)。</summary>
    public double NewWordRatio { get; init; } = 0.5;

    /// <summary>自動ミックスのモード比率(Q10)。</summary>
    public ModeWeights ModeWeights { get; init; } = new();

    public double FlashSeconds { get; init; } = 1.0;

    public IReadOnlyList<double> FlashSecondsChoices { get; init; } = [0.5, 1.0, 2.0];

    /// <summary>誤答単語1語あたりの追加再練習回数(Q3・Q7)。0 なら再練習なし。</summary>
    public int RetryCount { get; init; } = 1;

    public RetryTiming RetryTiming { get; init; } = RetryTiming.AfterNormalQuestions;
}

/// <summary>モード比率。キーの欠けで重みが 0 になる事故を防ぐため、辞書ではなく4項目の型にする。</summary>
public sealed record ModeWeights
{
    public int Japanese { get; init; } = 3;

    public int Audio { get; init; } = 3;

    public int Blank { get; init; } = 2;

    public int Flash { get; init; } = 2;

    public int this[LearningMode mode] => mode switch
    {
        LearningMode.Japanese => Japanese,
        LearningMode.Audio => Audio,
        LearningMode.Blank => Blank,
        LearningMode.Flash => Flash,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };
}

[JsonConverter(typeof(JsonStringEnumConverter<RetryTiming>))]
public enum RetryTiming
{
    /// <summary>通常問題をすべて終えてからまとめて再練習(Q9)。</summary>
    AfterNormalQuestions,
}

/// <summary>克服と定期再確認の設定。克服対象モードは設定にせず MasteryRules で固定する。</summary>
public sealed record MasterySettings
{
    /// <summary>克服に必要な、異なる日の連続正答回数(Q4)。</summary>
    public int RequiredCorrectDays { get; init; } = 2;

    /// <summary>克服後の再確認間隔(日)(Q14)。</summary>
    public IReadOnlyList<int> ReviewIntervalsDays { get; init; } = [7, 14, 30];

    /// <summary>最後の間隔を以後くり返すか(Q16: 30日ごと)。</summary>
    public bool RepeatLastInterval { get; init; } = true;

    /// <summary>
    /// 1回の練習に入れる再確認の上限(Q17)。問題数が UpTo 以下になる最初の区切りを使い、
    /// どれにも当てはまらないときは最後の区切りの値を使う。将来の任意問題数にも対応できる形。
    /// </summary>
    public IReadOnlyList<ReviewLimit> ReviewLimits { get; init; } =
    [
        new(5, 1),
        new(10, 1),
        new(20, 2),
    ];

    public int ReviewLimitFor(int questionCount)
    {
        if (ReviewLimits.Count == 0)
        {
            return 0;
        }

        foreach (var limit in ReviewLimits)
        {
            if (questionCount <= limit.UpTo)
            {
                return limit.Limit;
            }
        }

        return ReviewLimits[^1].Limit;
    }
}

public sealed record ReviewLimit(int UpTo, int Limit);

/// <summary>苦手登録と苦手度(v1.1 7.2)の設定。</summary>
public sealed record WeakWordScoreSettings
{
    /// <summary>通常問題の誤答が何回で苦手登録するか(Q12: 1回)。</summary>
    public int MarkWeakAfterMistakes { get; init; } = 1;

    public int MistakeCap { get; init; } = 5;

    public int RecentMistakeDays { get; init; } = 7;

    public int RecentMistakeBonus { get; init; } = 3;

    /// <summary>克服まであと1回(連続正答が必要数−1)の語への加点。</summary>
    public int PendingMasteryBonus { get; init; } = 2;

    public int OverdueDays { get; init; } = 14;

    public int OverdueBonus { get; init; } = 2;
}

public sealed record VocabularySettings
{
    public IReadOnlyList<string> EnabledSources { get; init; } = ["NGSL", "NAWL", "BSL"];

    public NewWordOrdering Ordering { get; init; } = NewWordOrdering.TierThenFrequency;
}

[JsonConverter(typeof(JsonStringEnumConverter<NewWordOrdering>))]
public enum NewWordOrdering
{
    /// <summary>Tier → 頻度順 → 同順位はランダム(v1.1 14.1)。</summary>
    TierThenFrequency,
}
