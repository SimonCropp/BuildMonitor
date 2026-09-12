/// <summary>
/// What the listing verbs return. Composed from the same rows and timings the screen shows, so
/// an AI assistant reading the list sees what the user sees.
/// </summary>
record BuildDto(
    string Key,
    string Connection,
    string Pipeline,
    string Repo,
    string? Branch,
    string Run,
    string Status,
    string? StatusText,
    DateTimeOffset? Started,
    DateTimeOffset? Finished,
    double Progress,
    string Timing,
    string BuildUrl,
    string? BranchUrl,
    string? PullRequest,
    string? PullRequestUrl,
    string? Commit,
    string? CommitMessage,
    string? Author,
    bool CanRetry,
    bool CanCancel);

record ConnectionDto(
    string Id,
    string Name,
    string Provider,
    string Health,
    string? Error,
    DateTimeOffset? LastPolled,
    int Pipelines);

record SummaryDto(
    int Connections,
    int Pipelines,
    int Failing,
    int Running,
    string TrayIcon,
    string Status,
    IReadOnlyList<ConnectionDto> ConnectionHealth);

[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(BuildDto))]
[JsonSerializable(typeof(List<BuildDto>))]
[JsonSerializable(typeof(List<ConnectionDto>))]
[JsonSerializable(typeof(SummaryDto))]
partial class DtoContext : JsonSerializerContext;
