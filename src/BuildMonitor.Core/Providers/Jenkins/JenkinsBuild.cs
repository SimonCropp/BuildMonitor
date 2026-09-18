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

    /// <summary>
    /// One build parameter, matched without case because its name is whatever the job that
    /// declared it called it. A build's parameters arrive in one of its actions, among actions
    /// that carry none.
    /// </summary>
    public string? Parameter(string name) =>
        (Actions ?? [])
        .SelectMany(_ => _.Parameters ?? [])
        .FirstOrDefault(_ => string.Equals(_.Name, name, StringComparison.OrdinalIgnoreCase))
        ?.Text();
}