/// <summary>
/// One provider's access and discovery, as the read tests share them.
/// </summary>
sealed class LiveSession(LiveConnection live, ProviderMemory memory, BuildAccess access, IReadOnlyList<Pipeline> pipelines, Pipeline? sandbox, string? sandboxProblem)
{
    public LiveConnection Live => live;

    public BuildAccess Access => access;

    public IReadOnlyList<Pipeline> Pipelines => pipelines;

    public Pipeline? Sandbox => sandbox;

    public string? SandboxProblem => sandboxProblem;

    public ProviderContext Context(ETagCache? cache = null, HttpMessageHandler? through = null) =>
        live.Context(memory, cache, through, access);

    /// <summary>
    /// What the fetch tests read: the sandbox's poll group first, then the first of the others, in
    /// discovery order. That is the order the app spends a fresh quota in. Fetching every group of
    /// a large account would spend the quota the tests are there to protect.
    /// </summary>
    public IReadOnlyList<PollGroup> Groups(int count)
    {
        var unit = live.Descriptor.FetchUnit;
        var groups = PollGroup.Of(unit, pipelines);
        if (sandbox is null)
        {
            return [..groups.Take(count)];
        }

        var key = PollGroup.KeyOf(unit, sandbox);
        return
        [
            ..groups.Where(_ => _.Key == key),
            ..groups.Where(_ => _.Key != key).Take(count - 1)
        ];
    }

    /// <summary>
    /// Each group fetched on its own, as the poller fetches them.
    /// </summary>
    public async Task<IReadOnlyList<Build>> Fetch(ProviderContext context, IReadOnlyList<PollGroup> groups, Cancel cancel)
    {
        var builds = new List<Build>();
        foreach (var group in groups)
        {
            builds.AddRange(await live.Provider.FetchBuilds(context, group.Pipelines, ConnectionPoller.PerPipeline, cancel));
        }

        return builds;
    }
}
