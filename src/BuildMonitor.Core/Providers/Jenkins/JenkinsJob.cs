class JenkinsJob
{
    public List<JenkinsBuild> Builds { get; set; } = [];
    public bool InQueue { get; set; }
    public JenkinsQueueItem? QueueItem { get; set; }
}