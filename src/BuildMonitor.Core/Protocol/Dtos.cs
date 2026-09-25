/// <summary>
/// What the listing verbs return. Composed from the same rows and timings the screen shows, so
/// an AI assistant reading the list sees what the user sees.
/// <para>
/// The MCP tools write nulls, as their output schema requires every member without a default. A
/// member with one is optional there, and its JsonIgnore keeps it out rather than null on every
/// build.
/// </para>
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
    // Whether this run is still in a queue its service can be asked to reorder, so it could be made
    // the next one to start.
    bool CanRunNext = false,
    // Where this repository is checked out under the code directory, or absent when it is not one
    // the tray found. An assistant reading a failure can open the code it broke without being told
    // where it lives.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Directory = null,
    // True on a run on another branch than its pipeline's own, a pull request's or a pushed
    // branch's, listed after the pipeline's own run while it is running, queued or failed. Absent
    // on the pipeline's own run. Its failure is not its pipeline failing: an assistant told the
    // pipeline was red would go looking for a break on main that is not there.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? OtherBranch = null,
    // When the user's deferral of this failure ends. Present only on a failed build put off with
    // Defer: it is left out of list_builds and the failing counts until then, so an assistant told
    // nothing would take the pipeline for green, or this run for one that never existed.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? DeferredUntil = null);

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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
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
    IReadOnlyList<ConnectionDto> ConnectionHealth,
    // Failures the user put off, which Failing leaves out as the tray does.
    int Deferred = 0);

/// <summary>
/// What a triage collected for one build: where the files are, and what did not make it.
/// <para>
/// Paths rather than bytes. The protocol base64s a whole message into one line and buffers it on
/// both sides, so an archive through it would sit in memory twice and expanded; whoever asked reads
/// the files from disk instead.
/// </para>
/// </summary>
record TriageFilesDto(
    // The directory holding them all, absolute and in the platform's own form. Empty when the build
    // had neither a log nor an artifact, so nothing was written.
    string Directory,
    // File names inside Directory, log.txt first where the build had a log.
    IReadOnlyList<string> Files,
    IReadOnlyList<SkippedArtifactDto> Skipped,
    // Why there are no artifacts, where the reason is not that the build published none: absent
    // when the service was asked and answered. An assistant must not read "none" off a service
    // nobody could ask.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Unsupported = null);

record SkippedArtifactDto(string Name, long? Bytes, string Reason);

[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TriageFilesDto))]
[JsonSerializable(typeof(BuildDto))]
[JsonSerializable(typeof(List<BuildDto>))]
[JsonSerializable(typeof(List<ConnectionDto>))]
[JsonSerializable(typeof(List<PipelineDto>))]
[JsonSerializable(typeof(SummaryDto))]
partial class DtoContext : JsonSerializerContext;
