class TeamCityBuild
{
    public long Id { get; set; }
    public string? Number { get; set; }
    public string? Status { get; set; }
    public string? State { get; set; }
    public string? BranchName { get; set; }
    public bool? DefaultBranch { get; set; }
    public string WebUrl { get; set; } = "";
    public string? StatusText { get; set; }
    public string? QueuedDate { get; set; }
    public string? StartDate { get; set; }
    public string? FinishDate { get; set; }
    public string? BuildTypeId { get; set; }
    public TeamCityCanceledInfo? CanceledInfo { get; set; }

    [JsonPropertyName("running-info")]
    public TeamCityRunningInfo? RunningInfo { get; set; }

    public TeamCityTriggered? Triggered { get; set; }
    public TeamCityRevisions? Revisions { get; set; }
    public TeamCityProperties? Properties { get; set; }

    /// <summary>
    /// One build parameter, matched without case because its name is whatever the build
    /// configuration that declared it called it.
    /// </summary>
    public string? Property(string name) =>
        Properties?.Property
            .FirstOrDefault(_ => string.Equals(_.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.Value;
}