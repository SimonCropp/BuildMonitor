class GitHubRepository
{
    public string FullName { get; set; } = "";
    public string HtmlUrl { get; set; } = "";
    public bool Archived { get; set; }
    public bool Disabled { get; set; }
    public DateTimeOffset? PushedAt { get; set; }
}

class GitHubWorkflows
{
    public List<GitHubWorkflow> Workflows { get; set; } = [];
}

class GitHubWorkflow
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string State { get; set; } = "";
}

class GitHubRuns
{
    public List<GitHubRun> WorkflowRuns { get; set; } = [];
}

class GitHubRun
{
    public long Id { get; set; }
    public long WorkflowId { get; set; }
    public long RunNumber { get; set; }
    public string Status { get; set; } = "";
    public string? Conclusion { get; set; }
    public string? HeadBranch { get; set; }
    public string? HeadSha { get; set; }
    public string? DisplayTitle { get; set; }
    public string HtmlUrl { get; set; } = "";
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? RunStartedAt { get; set; }
    public GitHubActor? Actor { get; set; }
    public GitHubCommit? HeadCommit { get; set; }
    public List<GitHubPullRequest> PullRequests { get; set; } = [];
}

class GitHubActor
{
    public string? Login { get; set; }
}

class GitHubCommit
{
    public string? Message { get; set; }
}

class GitHubPullRequest
{
    public long Number { get; set; }
}

class GitHubUser
{
    public string Login { get; set; } = "";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<GitHubRepository>))]
[JsonSerializable(typeof(GitHubWorkflows))]
[JsonSerializable(typeof(GitHubRuns))]
[JsonSerializable(typeof(GitHubUser))]
partial class GitHubContext : JsonSerializerContext;
