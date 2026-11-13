using EikenSpelling.Persistence;

namespace EikenSpelling.Flow.Tests;

/// <summary>各画面で受け付けない操作は例外になり、画面もファイルも変わらない(app-flow design 4.1)。</summary>
public class FlowInvalidOperationTests
{
    public enum Op
    {
        Reload,
        StartFresh,
        Resume,
        Discard,
        StartPractice,
        Answer,
        RetrySave,
        Next,
        Quit,
        GoHome,
    }

    public enum At
    {
        Home,
        ResumePrompt,
        ResumePromptCannotResume,
        Question,
        FeedbackSaved,
        FeedbackUnsaved,
        Result,
        BothCorrupt,
        FatalNewer,
        FatalUnavailable,
        FatalFolder,
        FatalWordSet,
    }

    private static readonly Dictionary<At, Op[]> Allowed = new()
    {
        [At.Home] = [Op.StartPractice],
        [At.ResumePrompt] = [Op.Resume, Op.Discard],
        [At.ResumePromptCannotResume] = [Op.Discard],
        [At.Question] = [Op.Answer, Op.Quit],
        [At.FeedbackSaved] = [Op.Next],
        [At.FeedbackUnsaved] = [Op.RetrySave],
        [At.Result] = [Op.GoHome],
        [At.BothCorrupt] = [Op.StartFresh],
        [At.FatalNewer] = [],
        [At.FatalUnavailable] = [Op.Reload],
        [At.FatalFolder] = [],
        [At.FatalWordSet] = [],
    };

    public static TheoryData<At, Op> Rejected()
    {
        var data = new TheoryData<At, Op>();
        foreach (var (at, allowed) in Allowed)
        {
            foreach (var op in Enum.GetValues<Op>().Except(allowed))
            {
                data.Add(at, op);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Rejected))]
    public void Operation_not_accepted_on_screen_throws_and_changes_nothing(At at, Op op)
    {
        using var env = new FlowEnv();
        var flow = Arrange(env, at);
        var screen = flow.Screen;
        var files = env.Dir.Snapshot();

        Assert.Throws<InvalidOperationException>(() => Do(flow, op));

        Assert.Same(screen, flow.Screen);
        Assert.Equal(files, env.Dir.Snapshot());
    }

    [Fact]
    public void Next_twice_is_rejected_the_second_time()
    {
        using var env = new FlowEnv();
        var flow = Arrange(env, At.FeedbackSaved);
        flow.Next();

        Assert.Throws<InvalidOperationException>(flow.Next);
    }

    private static PracticeFlow Arrange(FlowEnv env, At at)
    {
        switch (at)
        {
            case At.BothCorrupt:
                env.Dir.Write(ProgressRepository.FileName, "{ broken");
                env.Dir.Write("progress.bak.json", "{ broken");
                return env.Start();
            case At.FatalNewer:
                env.Dir.Write(ProgressRepository.FileName, """{ "schemaVersion": 99 }""");
                return env.Start();
            case At.FatalUnavailable:
                env.Dir.Write(ProgressRepository.FileName, FlowEnv.EmptyProgressJson);
                env.Files.FailReadsOf.Add(ProgressRepository.FileName);
                return env.Start();
            case At.FatalFolder:
                return PracticeFlow.Start(env.Options() with { DataDirectory = "relative" });
            case At.FatalWordSet:
                return PracticeFlow.Start(env.Options() with { WordSetJson = "{ broken" });
            case At.ResumePromptCannotResume:
                env.Start().StartPractice(Choices.Auto(count: 1));
                env.WordsJson = Words.Json(new TestWord("zzz"));
                return env.Start();
        }

        var flow = env.Start();
        if (at == At.Home)
        {
            return flow;
        }

        flow.StartPractice(Choices.Auto(count: 1));
        switch (at)
        {
            case At.Question:
                return flow;
            case At.ResumePrompt:
                flow.Quit();
                return flow;
            case At.FeedbackSaved:
                flow.AnswerCurrent(true);
                return flow;
            case At.FeedbackUnsaved:
                env.Files.FailWrites = true;
                flow.AnswerCurrent(true);
                return flow;
            default:
                flow.AnswerCurrent(true);
                flow.Next();
                return flow; // Result
        }
    }

    private static void Do(PracticeFlow flow, Op op)
    {
        switch (op)
        {
            case Op.Reload: flow.Reload(); break;
            case Op.StartFresh: flow.StartFresh(); break;
            case Op.Resume: flow.Resume(); break;
            case Op.Discard: flow.DiscardAndGoHome(); break;
            case Op.StartPractice: flow.StartPractice(Choices.Auto()); break;
            case Op.Answer: flow.Answer("apple"); break;
            case Op.RetrySave: flow.RetrySave(); break;
            case Op.Next: flow.Next(); break;
            case Op.Quit: flow.Quit(); break;
            case Op.GoHome: flow.GoHome(); break;
        }
    }
}
