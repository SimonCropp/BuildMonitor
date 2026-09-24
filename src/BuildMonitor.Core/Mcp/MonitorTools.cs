/// <summary>
/// What the MCP tools do, over the protocol, with no MCP types in sight so it can be tested
/// against an in-process tray.
/// </summary>
sealed class MonitorTools(IProtocolClient client)
{
    public Task<List<BuildDto>> ListBuilds(string? filter, Cancel cancel) =>
        ListBuilds(filter, false, cancel);

    /// <summary>
    /// With <paramref name="includeDeferred"/>, the failures the user put off follow the rest, each
    /// carrying when its deferral ends. Left out by default, as the window leaves them out.
    /// </summary>
    public async Task<List<BuildDto>> ListBuilds(string? filter, bool includeDeferred, Cancel cancel)
    {
        var builds = await Read(new(Verb.List), DtoContext.Default.ListBuildDto, cancel);
        if (includeDeferred)
        {
            builds.AddRange(await ListDeferred(null, cancel));
        }

        if (string.IsNullOrWhiteSpace(filter))
        {
            return builds;
        }

        return builds
            .Where(_ => Matches(_, filter))
            .ToList();
    }

    /// <summary>
    /// The failures the user put off for a few days, which list_builds and the failing counts leave
    /// out until the deferral ends or the pipeline passes.
    /// </summary>
    public async Task<List<BuildDto>> ListDeferred(string? filter, Cancel cancel)
    {
        var builds = await Read(new(Verb.Deferred), DtoContext.Default.ListBuildDto, cancel);
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

    /// <summary>
    /// The pipelines whose own run failed, as the tray and the summary count them. A pull request
    /// failing is in the list of builds, marked as another branch, but not here: the triage prompt
    /// works through these a pipeline at a time, and one repository's main and its pull request
    /// would read as two repositories broken by one pipeline.
    /// </summary>
    public async Task<List<BuildDto>> ListFailing(string? filter, Cancel cancel)
    {
        var builds = await Read(new(Verb.List), DtoContext.Default.ListBuildDto, cancel);
        var failed = builds.Where(_ => _.Status == nameof(BuildStatus.Failed) &&
                                       _.OtherBranch != true);
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
        if (connectionId is null)
        {
            return "Refreshing every connection";
        }

        return $"Refreshing {connectionId}";
    }

    public Task<string> RetryBuild(string key, Cancel cancel) =>
        Send(new(Verb.Retry, key), cancel);

    public Task<string> CancelBuild(string key, Cancel cancel) =>
        Send(new(Verb.Cancel, key), cancel);

    public Task<string> RunBuildNext(string key, Cancel cancel) =>
        Send(new(Verb.RunNext, key), cancel);

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
        var body = response.Body;

        if (!response.Ok)
        {
            throw new InvalidOperationException(body);
        }

        if (body.Length == 0)
        {
            return "Done";
        }

        return body;
    }
}
