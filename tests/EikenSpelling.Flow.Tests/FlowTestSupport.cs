using System.Text.Json;

using EikenSpelling.Core.Application;
using EikenSpelling.Core.Models;
using EikenSpelling.Persistence;
using EikenSpelling.Persistence.Tests;

using static EikenSpelling.Core.Models.LearningMode;

namespace EikenSpelling.Flow.Tests;

/// <summary>失敗を入れたり外したりできるファイル操作。それ以外は本物の System.IO。権限には頼らない(root では効かない)。</summary>
internal sealed class SwitchableFiles : IFileOperations
{
    private readonly IFileOperations _inner = SystemFileOperations.Instance;

    /// <summary>真の間、すべての書き込みが失敗する(保存の失敗)。</summary>
    public bool FailWrites { get; set; }

    /// <summary>このファイル名(拡張子込み)の読み込みが失敗する(Unavailable)。</summary>
    public HashSet<string> FailReadsOf { get; } = new(StringComparer.Ordinal);

    public int Writes { get; private set; }

    public bool Exists(string path) => _inner.Exists(path);

    public long Length(string path) => _inner.Length(path);

    public string ReadAllText(string path) =>
        FailReadsOf.Contains(Path.GetFileName(path)) ? throw new IOException("読めない(テスト)") : _inner.ReadAllText(path);

    public void WriteAndFlush(string path, string content)
    {
        Writes++;
        if (FailWrites)
        {
            throw new IOException("書けない(テスト)");
        }

        _inner.WriteAndFlush(path, content);
    }

    public void Replace(string source, string destination, string backup) => _inner.Replace(source, destination, backup);

    public void Move(string source, string destination) => _inner.Move(source, destination);

    public void Delete(string path) => _inner.Delete(path);

    public void CreateDirectory(string path) => _inner.CreateDirectory(path);
}

/// <summary>テスト用の単語データ。</summary>
internal sealed record TestWord(string Id, string Ja = "訳", string? Blanks = null, bool Audio = true, bool Japanese = true);

internal static class Words
{
    public static readonly TestWord[] Five =
        [new("apple"), new("banana"), new("cherry"), new("grape"), new("lemon")];

    public static string Json(params TestWord[] words) => Json("v1", words);

    public static string Json(string version, params TestWord[] words) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        wordSetVersion = version,
        sourceVersion = "src1",
        words = words.Select(w => new Dictionary<string, object?>
        {
            ["id"] = w.Id,
            ["word"] = w.Id,
            ["ja"] = w.Ja,
            ["blanks"] = w.Blanks,
            ["audioEligible"] = w.Audio,
            ["japaneseEligible"] = w.Japanese,
            ["sources"] = new[] { "NGSL" },
        }),
    });
}

/// <summary>1つのテストの環境(一時フォルダ・固定時刻・連番の ID・固定シードの乱数)。</summary>
internal sealed class FlowEnv : IDisposable
{
    public static readonly DateTimeOffset T0 = new(2026, 11, 1, 10, 0, 0, TimeSpan.FromHours(9));

    private int _ids;

    public TempDir Dir { get; } = new();

    public FixedTimeProvider Time { get; } = new(T0);

    public SwitchableFiles Files { get; } = new();

    public string WordsJson { get; set; } = Words.Json(Words.Five);

    public HashSet<LearningMode> Modes { get; set; } = [Japanese, Audio, Blank, Flash];

    public static string EmptyProgressJson => ProgressJson.Serialize(new ProgressDocument());

    public FlowOptions Options() => new()
    {
        DataDirectory = Dir.Path,
        WordSetJson = WordsJson,
        AvailableModes = Modes,
        Time = Time,
        Random = new Random(1),
        NewSessionId = () => new Guid(++_ids, 0, 0, new byte[8]),
        Files = Files,
        RetryDelay = TimeSpan.Zero,
    };

    public PracticeFlow Start() => PracticeFlow.Start(Options());

    public string? ProgressJsonText => Dir.Read(ProgressRepository.FileName);

    public ProgressDocument Progress() => ProgressJson.Parse(ProgressJsonText ?? throw new InvalidOperationException("progress.json がありません。"));

    /// <summary>学習記録に入っている回答の総数(通常・再練習)。</summary>
    public int RecordedAnswers() => Progress().Progress.Words.Values
        .SelectMany(w => w.Modes.Values)
        .Sum(m => m.NormalCorrect + m.NormalWrong + m.RetryCorrect + m.RetryWrong);

    public void Dispose() => Dir.Dispose();
}

internal static class Choices
{
    public static PracticeChoice Auto(int count = 5, bool weakOnly = false, double flash = 1.0) =>
        new(true, Japanese, count, weakOnly, flash);

    public static PracticeChoice Manual(LearningMode mode, int count = 5) => new(false, mode, count, false, 1.0);
}

internal static class FlowExtensions
{
    public static T As<T>(this PracticeFlow flow) where T : FlowScreen => Assert.IsType<T>(flow.Screen);

    /// <summary>今の問題に正答(または誤答)する。</summary>
    public static FeedbackScreen AnswerCurrent(this PracticeFlow flow, bool correct)
    {
        var q = flow.As<QuestionScreen>().Question;
        flow.Answer(correct ? q.Word : q.Word + "x");
        return flow.As<FeedbackScreen>();
    }

    /// <summary>結果画面まで、すべて指定どおりに答える。</summary>
    public static ResultScreen AnswerAll(this PracticeFlow flow, Func<QuestionView, bool> correct)
    {
        while (flow.Screen is QuestionScreen q)
        {
            flow.AnswerCurrent(correct(q.Question));
            flow.Next();
        }

        return flow.As<ResultScreen>();
    }
}
