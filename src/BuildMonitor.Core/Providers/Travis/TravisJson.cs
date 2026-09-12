class TravisRepositories
{
    public List<TravisRepository> Repositories { get; set; } = [];
}

class TravisRepository
{
    public long Id { get; set; }
    public string Slug { get; set; } = "";
}

class TravisBuilds
{
    public List<TravisBuild> Builds { get; set; } = [];
}

class TravisBuild
{
    public long Id { get; set; }
    public string Number { get; set; } = "";
    public string State { get; set; } = "";
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public long? PullRequestNumber { get; set; }
    public TravisBranch? Branch { get; set; }
    public TravisCommit? Commit { get; set; }
}

class TravisBranch
{
    public string Name { get; set; } = "";
}

class TravisCommit
{
    public string? Sha { get; set; }
    public string? Message { get; set; }
    public TravisAuthor? Author { get; set; }
}

class TravisAuthor
{
    public string? Name { get; set; }
}

class TravisUser
{
    public string Login { get; set; } = "";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(TravisRepositories))]
[JsonSerializable(typeof(TravisBuilds))]
[JsonSerializable(typeof(TravisUser))]
partial class TravisContext : JsonSerializerContext;
