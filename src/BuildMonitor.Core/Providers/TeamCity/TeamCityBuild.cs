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
}