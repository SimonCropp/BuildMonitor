/// <summary>
/// Which failed branches a connection asks the service it holds repositories on about, each cycle:
/// those of a repository on its host with no answer yet, or none that speaks for their newest run,
/// or an answer that has aged. One question a repository, branch and pull request, however many of
/// its workflows failed on it.
/// </summary>
static class BranchQuestions
{
    /// <summary>
    /// How long an answer that a pull request is open, or a branch still there, stands before it is
    /// asked again. Merging, closing and deleting happen at any time, and a failed branch lingers
    /// this long at most after one. A merged, closed or deleted one is never asked again, as nothing
    /// brings that back but a new run, which is asked about as its own.
    /// </summary>
    public static readonly TimeSpan OpenFor = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long a connection that could not say leaves the question before it asks again, as after
    /// its token is given the rights it lacked. Another connection is not held back by it, since
    /// its credential may see what this one cannot.
    /// </summary>
    public static readonly TimeSpan UnknownFor = TimeSpan.FromHours(1);

    /// <param name="unanswered">The questions this connection could not answer, and when it tried.</param>
    public static ImmutableArray<BranchQuestion> Of(ImmutableArray<PipelineBuilds> pipelines, ImmutableDictionary<string, BranchVerdict> verdicts, Connection connection, IReadOnlyDictionary<string, DateTimeOffset> unanswered, DateTimeOffset now)
    {
        var questions = ImmutableArray.CreateBuilder<BranchQuestion>();
        var asked = new HashSet<string>();
        foreach (var run in BranchVerdicts.Failed(pipelines))
        {
            if (run.RepoUrl is not { } repository ||
                run.Branch is not { } branch)
            {
                continue;
            }

            var question = new BranchQuestion(repository, branch, run.PullRequestNumber);
            var key = question.Key;
            // Asked once however many workflows failed on it, and marked asked only once due: two
            // workflows' runs of one branch can start either side of an answer.
            if ((unanswered.TryGetValue(key, out var tried) && now - tried < UnknownFor) ||
                !Due(BranchVerdicts.For(verdicts, run), now) ||
                !BranchHosts.Answers(connection, repository) ||
                !asked.Add(key))
            {
                continue;
            }

            questions.Add(question);
        }

        return questions.ToImmutable();
    }

    /// <summary>
    /// Whether an answer, or the lack of one, leaves the question to ask. One that could not say is
    /// asked again by any connection that has not itself failed to answer it.
    /// </summary>
    static bool Due(BranchVerdict? verdict, DateTimeOffset now) =>
        verdict switch
        {
            null => true,
            { Fate: BranchFate.Open } => now - verdict.At >= OpenFor,
            { Fate: BranchFate.Unknown } => true,
            _ => false
        };
}
