class JenkinsJob
{
    public List<JenkinsBuild> Builds { get; set; } = [];
    // Its number and estimate only, as the one build an estimate is asked of.
    public JenkinsBuild? LastBuild { get; set; }
    public bool InQueue { get; set; }
    public JenkinsQueueItem? QueueItem { get; set; }
}