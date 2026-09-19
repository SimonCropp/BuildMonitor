/// <summary>
/// What the MCP tools do, over the protocol, with no MCP types in sight so it can be tested
/// against an in-process tray.
/// </summary>
sealed class MonitorTools(IProtocolClient client)
{
    public async Task<List<BuildDto>> ListBuilds(string? filter, Cancel cancel)
    {
        var builds = await Read(new(Verb.List), DtoContext.Default.ListBuildDto, cancel);
        if (string.IsNullOrWhiteSpace(filter))
        {
            return builds;
        }

        return builds
            .Where(_ => Matches(_, filter))
            .ToList();
    }

    static bool Matches(BuildDto build, string filter) =>
        build.Pipeline.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        build.Repo.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        build.Connection.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        (build.Branch?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);

    public async Task<List<BuildDto>> ListFailing(string? filter, Cancel cancel)
    {
        var builds = await Read(new(Verb.List), DtoContext.Default.ListBuildDto, cancel);
        var failed = builds.Where(_ => _.Status == nameof(BuildStatus.Failed));
        if (string.IsNullOrWhiteSpace(filter))
        {
            return failed.ToList();
        }

        return failed
            .Where(_ => Matches(_, filter))
            .ToList();
    }

    public Task<BuildDto> GetBuild(string key, Cancel cancel) =>
        Read(new(Verb.Get, key), DtoContext.Default.BuildDto, cancel);

    /// <summary>
    /// The runs of one pipeline rather than the one row it shows. Runs on the same branch share
    /// a key, which is the row's, and are told apart by their run number.
    /// </summary>
    public Task<List<BuildDto>> ListRuns(string key, Cancel cancel) =>
        Read(new(Verb.Runs, key), DtoContext.Default.ListBuildDto, cancel);

    public Task<List<PipelineDto>> ListPipelines(Cancel cancel) =>
        Read(new(Verb.Pipelines), DtoContext.Default.ListPipelineDto, cancel);

    public Task<SummaryDto> Summary(Cancel cancel) =>
        Read(new(Verb.Summary), DtoContext.Default.SummaryDto, cancel);

    public Task<List<ConnectionDto>> ListConnections(Cancel cancel) =>
        Read(new(Verb.Connections), DtoContext.Default.ListConnectionDto, cancel);

    public async Task<string> Refresh(string? connectionId, Cancel cancel)
    {
        await Send(new(Verb.Refresh, connectionId), cancel);
        return connectionId is null ? "Refreshing every connection" : $"Refreshing {connectionId}";
    }

    public Task<string> RetryBuild(string key, Cancel cancel) =>
        Send(new(Verb.Retry, key), cancel);

    public Task<string> CancelBuild(string key, Cancel cancel) =>
        Send(new(Verb.Cancel, key), cancel);

    public Task<string> OpenBuild(string key, string which, Cancel cancel) =>
        Send(new(Verb.Open, key, which), cancel);

    /// <summary>
    /// The log as the text the CI service wrote, rather than JSON: its sections and indentation
    /// are what an assistant reads the failure out of, and escaping them buys nothing.
    /// </summary>
    public Task<string> GetLog(string key, int maxLines, Cancel cancel) =>
        Send(new(Verb.Log, key, maxLines.ToString(CultureInfo.InvariantCulture)), cancel);

    /// <summary>
    /// The files rather than their bytes: the protocol base64s a whole message into one line and
    /// buffers it on both sides, so an archive through it would sit in memory twice and expanded.
    /// Whoever asked reads them from disk with its own file tools.
    /// </summary>
    public Task<TriageFilesDto> DownloadArtifacts(string key, Cancel cancel) =>
        Read(new(Verb.Triage, key), DtoContext.Default.TriageFilesDto, cancel);

    async Task<T> Read<T>(Message message, JsonTypeInfo<T> info, Cancel cancel)
    {
        var response = await client.Send(message, cancel);
        if (!response.Ok)
        {
            throw new InvalidOperationException(response.Body);
        }

        var read = JsonSerializer.Deserialize(response.Body, info);
        if (read == null)
        {
            throw new InvalidOperationException("Empty response");
        }

        return read;
    }

    async Task<string> Send(Message message, Cancel cancel)
    {
        var response = await client.Send(message, cancel);
        if (!response.Ok)
        {
            throw new InvalidOperationException(response.Body);
        }

        if (response.Body.Length == 0)
        {
            return "Done";
        }

        return response.Body;
    }
}
