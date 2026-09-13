class JenkinsBuild
{
    public long Number { get; set; }
    public string Url { get; set; } = "";
    public string? Result { get; set; }
    public bool Building { get; set; }
    public long? Timestamp { get; set; }
    public long? Duration { get; set; }
    public long? EstimatedDuration { get; set; }
    public List<JenkinsAction>? Actions { get; set; }
}