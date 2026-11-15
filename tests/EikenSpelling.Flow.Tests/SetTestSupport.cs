using System.Collections.Immutable;

using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;
using EikenSpelling.Persistence;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Flow.Tests;

/// <summary>乱数の呼び出しを数える(確認後に Selector を撃ち直していないことの確認)。</summary>
internal sealed class CountingRandom(int seed) : Random(seed)
{
    public int Calls { get; private set; }

    public override int Next()
    {
        Calls++;
        return base.Next();
    }

    public override int Next(int maxValue)
    {
        Calls++;
        return base.Next(maxValue);
    }

    public override int Next(int minValue, int maxValue)
    {
        Calls++;
        return base.Next(minValue, maxValue);
    }

    public override double NextDouble()
    {
        Calls++;
        return base.NextDouble();
    }
}

/// <summary>語彙セットのテストの環境。発行した SessionId と乱数の呼び出しを数える。</summary>
internal sealed class SetHarness : IDisposable
{
    public SetHarness(int total = 120, Func<int, bool>? hasBlank = null, string version = "v1")
    {
        Env = new FlowEnv { WordsJson = Words.Json(version, Make(total, hasBlank)) };
    }

    public FlowEnv Env { get; }

    public List<Guid> Issued { get; } = [];

    public CountingRandom Random { get; } = new(1);

    /// <summary>w000, w001, ... 全部 Tier 3・頻度なし = 整列は ID の Ordinal 順(セット1 = w000〜w049)。</summary>
    public static TestWord[] Make(int total, Func<int, bool>? hasBlank = null, int firstIndex = 0) =>
        [.. Enumerable.Range(firstIndex, total).Select(i => new TestWord(Id(i), Blanks: hasBlank?.Invoke(i) ?? true ? "w_" + Id(i)[2..] : null))];

    public static string Id(int i) => Enc($"w{i:D3}");

    /// <summary>単語は英字だけ(WordSetValidator)なので、数字を a〜j に置き換える(順序は保たれる)。</summary>
    public static string Enc(string text) => string.Concat(text.Select(c => char.IsDigit(c) ? (char)('a' + (c - '0')) : c));

    public PracticeFlow Start() => PracticeFlow.Start(Options());

    public FlowOptions Options() => Env.Options() with
    {
        Random = Random,
        NewSessionId = () =>
        {
            var id = new Guid(Issued.Count + 100, 0, 0, new byte[8]);
            Issued.Add(id);
            return id;
        },
    };

    /// <summary>主ファイルの中身(無ければ null)。変更が無いことの確認用。</summary>
    public string? ProgressText => Env.ProgressJsonText;

    public void Seed(params WordProgress[] records) =>
        Env.Dir.Write(
            ProgressRepository.FileName,
            ProgressJson.Serialize(new ProgressDocument
            {
                WordSetVersion = "v1",
                SourceVersion = "src1",
                Progress = LearningProgress.Empty with { Words = records.ToImmutableDictionary(r => r.WordId, StringComparer.Ordinal) },
            }));

    public static WordProgress Weak(string id, int mistakes = 5) => new()
    {
        WordId = id,
        State = LearningState.Weak,
        LastNormalAskedAt = FlowEnv.T0.AddDays(-1),
        Modes = ImmutableDictionary<LearningMode, ModeRecord>.Empty.Add(
            Japanese, new ModeRecord { NormalWrong = mistakes, LastWrongAt = FlowEnv.T0.AddHours(-1) }),
    };

    public static WordProgress DueReview(string id) => new()
    {
        WordId = id,
        State = LearningState.Mastered,
        MasteryStreak = 2,
        ReviewStage = 1,
        NextReviewDate = new DateOnly(2026, 10, 30),
        LastEligibleCorrectDate = new DateOnly(2026, 10, 20),
        MasteredAt = FlowEnv.T0.AddDays(-10),
        LastNormalAskedAt = FlowEnv.T0.AddDays(-10),
    };

    public static WordProgress Normal(string id) =>
        new() { WordId = id, State = LearningState.Normal, LastNormalAskedAt = FlowEnv.T0.AddDays(-2) };

    public void Dispose() => Env.Dispose();
}

internal static class SetFlowExtensions
{
    public static VocabularySetScreen OpenSets(this PracticeFlow flow)
    {
        flow.OpenVocabularySets();
        return flow.As<VocabularySetScreen>();
    }

    /// <summary>主ファイルの中のセッション(無ければ null)。</summary>
    public static PracticeSession? SavedSession(this SetHarness h) => h.Env.Progress().Session;
}
