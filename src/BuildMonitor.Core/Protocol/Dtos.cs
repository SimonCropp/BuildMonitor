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
    bool CanCancel,
    // Where this repository is checked out under the code directory, or absent when it is not one
    // the tray found. An assistant reading a failure can open the code it broke without being told
    // where it lives.
    string? Directory = null);

/// <summary>
/// One monitored pipeline, including one that has produced no build inside the history window
/// and so has no row. <see cref="Runs"/> is how many of its runs the tray holds, and a zero is
/// the thing a list of builds cannot say: watched, but quiet.
/// </summary>
record PipelineDto(
    string Key,
    string Connection,
    string Name,
    string Repo,
    string? Group,
    string Url,
    int Runs,
    // As on a build: where this pipeline's repository is checked out, or absent.
    string? Directory = null);

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
[JsonSerializable(typeof(List<PipelineDto>))]
[JsonSerializable(typeof(SummaryDto))]
partial class DtoContext : JsonSerializerContext;
