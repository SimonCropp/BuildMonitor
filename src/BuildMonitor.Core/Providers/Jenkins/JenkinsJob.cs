class JenkinsJob
{
    public required List<JenkinsBuild> Builds { get; init; }
    // Its number and estimate only, as the one build an estimate is asked of.
    public JenkinsBuild? LastBuild { get; init; }
    public bool InQueue { get; init; }
    public JenkinsQueueItem? QueueItem { get; init; }
}