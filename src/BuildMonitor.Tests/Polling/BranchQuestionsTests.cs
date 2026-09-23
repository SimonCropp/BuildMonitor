/// <summary>
/// Which failed branches a connection asks about in a cycle. Each answer that could be the last
/// word stops the asking, and one that could change has it asked again after a while.
/// </summary>
public class BranchQuestionsTests
{
    static readonly Build inline = Fixtures.GitHubBuildsOnMain()[1];

    static ImmutableArray<BranchQuestion> Of(SessionState state, DateTimeOffset now, Connection? connection = null, IReadOnlyDictionary<string, DateTimeOffset>? unanswered = null) =>
        BranchQuestions.Of(RowProjection.Pipelines(state), state.Verdicts, connection ?? Fixtures.GitHub, unanswered ?? new Dictionary<string, DateTimeOffset>(), now);

    static SessionState Answered(BranchFate fate, DateTimeOffset at) =>
        Fixtures.WithDefaultBranches() with
        {
            Verdicts = ImmutableDictionary<string, BranchVerdict>.Empty.Add(BranchVerdicts.KeyOf(inline)!, new(fate, at))
        };

    [Test]
    public async Task AFailedBranchWithNoAnswerIsAsked()
    {
        var questions = Of(Fixtures.WithDefaultBranches(), Fixtures.Now);
        await Assert.That(questions).IsEquivalentTo([new BranchQuestion("https://github.com/VerifyTests/Verify", "feature/inline", "42")]);
    }

    /// <summary>
    /// A pull request found open can be merged or closed at any time, so it is asked about again once
    /// the answer has aged.
    /// </summary>
    [Test]
    public async Task AnOpenAnswerIsAskedAgainOnceItHasAged()
    {
        var state = Answered(BranchFate.Open, Fixtures.Now);
        await Assert.That(Of(state, Fixtures.Now + BranchQuestions.OpenFor - TimeSpan.FromSeconds(1))).IsEmpty();
        await Assert.That(Of(state, Fixtures.Now + BranchQuestions.OpenFor).Length).IsEqualTo(1);
    }

    /// <summary>
    /// Merged, closed or deleted is the last word about the runs it covers, and only a run started
    /// after it is asked about.
    /// </summary>
    [Test]
    public async Task AGoneAnswerIsFinalForTheRunsItCovers()
    {
        await Assert.That(Of(Answered(BranchFate.Merged, Fixtures.Now), Fixtures.Now + TimeSpan.FromDays(1))).IsEmpty();
        var before = inline.Started!.Value - TimeSpan.FromMinutes(1);
        await Assert.That(Of(Answered(BranchFate.Merged, before), Fixtures.Now).Length).IsEqualTo(1);
    }

    /// <summary>
    /// One connection not being able to say leaves another free to, whose credential may see more;
    /// the one that could not waits before trying again.
    /// </summary>
    [Test]
    public async Task AnAnswerThatCouldNotSayIsAskedByAnyConnectionThatHasNotTried()
    {
        var state = Answered(BranchFate.Unknown, Fixtures.Now);
        await Assert.That(Of(state, Fixtures.Now).Length).IsEqualTo(1);
        var tried = new Dictionary<string, DateTimeOffset>
        {
            [BranchVerdicts.KeyOf(inline)!] = Fixtures.Now
        };
        await Assert.That(Of(state, Fixtures.Now + TimeSpan.FromMinutes(59), unanswered: tried)).IsEmpty();
        await Assert.That(Of(state, Fixtures.Now + BranchQuestions.UnknownFor, unanswered: tried).Length).IsEqualTo(1);
    }

    /// <summary>
    /// A connection is only asked about repositories on its own host.
    /// </summary>
    [Test]
    public async Task ARepositoryOnAnotherHostIsNotAsked()
    {
        var enterprise = Fixtures.GitHub with
        {
            Server = "https://github.example.com"
        };
        await Assert.That(Of(Fixtures.WithDefaultBranches(), Fixtures.Now, enterprise)).IsEmpty();
        await Assert.That(Of(Fixtures.WithDefaultBranches(), Fixtures.Now, Fixtures.Jenkins)).IsEmpty();
    }

    /// <summary>
    /// Every workflow that failed on a pull request is answered by one question about it.
    /// </summary>
    [Test]
    public async Task OneQuestionForEveryWorkflowThatFailedOnIt()
    {
        var docs = inline with
        {
            PipelineId = "Verify/docs.yml",
            PipelineName = "docs.yml"
        };
        var main = Fixtures.GitHubBuildsOnMain()[2] with
        {
            PipelineId = "Verify/docs.yml",
            PipelineName = "docs.yml"
        };
        var state = MonitorSession.ApplyPoll(Fixtures.WithDefaultBranches(), Fixtures.GitHub.Id, [], [.. Fixtures.GitHubBuildsOnMain(), docs, main], Fixtures.Now);
        await Assert.That(Of(state, Fixtures.Now).Length).IsEqualTo(1);
    }

    /// <summary>
    /// With other branches hidden there is nothing to fold, so nothing is asked.
    /// </summary>
    [Test]
    public async Task NothingIsAskedWhileOtherBranchesAreHidden()
    {
        var state = Fixtures.WithDefaultBranches();
        var hidden = MonitorSession.ApplySettings(
            state,
            state.Settings with
            {
                ShowOtherBranches = false
            });
        await Assert.That(Of(hidden, Fixtures.Now)).IsEmpty();
    }
}
