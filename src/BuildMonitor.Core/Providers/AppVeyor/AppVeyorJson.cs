class AppVeyorProject
{
    public string AccountName { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string? RepositoryType { get; set; }
    public string? RepositoryName { get; set; }
}

class AppVeyorHistory
{
    public List<AppVeyorBuild> Builds { get; set; } = [];
}

class AppVeyorBuild
{
    public long BuildId { get; set; }
    public long BuildNumber { get; set; }
    public string Version { get; set; } = "";
    public string? Branch { get; set; }
    public string? CommitId { get; set; }
    public string? Message { get; set; }
    public string? AuthorName { get; set; }
    public string? PullRequestId { get; set; }
    public string Status { get; set; } = "";
    public DateTimeOffset? Created { get; set; }
    public DateTimeOffset? Started { get; set; }
    public DateTimeOffset? Finished { get; set; }
}

record AppVeyorRerun(long BuildId, bool ReRunIncomplete);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<AppVeyorProject>))]
[JsonSerializable(typeof(AppVeyorHistory))]
[JsonSerializable(typeof(AppVeyorRerun))]
partial class AppVeyorContext : JsonSerializerContext;
