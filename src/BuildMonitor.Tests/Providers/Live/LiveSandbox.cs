/// <summary>
/// The one pipeline per account that the live tests may retry and cancel, and how the app would
/// fetch it.
/// </summary>
static class LiveSandbox
{
    /// <summary>
    /// The pipeline _PIPELINE names, by id, else by name or repository and name. It must match
    /// exactly one pipeline. A name matching two would let the action round cancel a build nobody
    /// set aside for it. With no _PIPELINE, there is no sandbox and no problem.
    /// </summary>
    public static (Pipeline? Pipeline, string? Problem) Find(LiveConnection live, IReadOnlyList<Pipeline> pipelines)
    {
        if (live.Pipeline is not { } wanted)
        {
            return (null, null);
        }

        var byId = pipelines.Where(_ => Same(_.Id, wanted)).ToList();
        if (byId.Count == 1)
        {
            return (byId[0], null);
        }

        var byName = pipelines
            .Where(_ => Same(_.Name, wanted) ||
                        Same($"{_.RepoName}/{_.Name}", wanted))
            .ToList();
        if (byName.Count == 1)
        {
            return (byName[0], null);
        }

        var problem = $"{live.Id}: {byName.Count} of the {pipelines.Count} pipelines discovered match {LiveSettings.Prefix(live.Id)}PIPELINE '{wanted}'.";
        if (live.Id == "github")
        {
            problem += " GitHub discovery leaves out forks and repositories not pushed to in 90 days.";
        }

        return (null, problem);
    }

    /// <summary>
    /// Every pipeline fetched with the sandbox in one poll. GitHub sizes a page by the workflows it
    /// is asked for, and Octopus reads a space's whole dashboard. So fetching the sandbox alone
    /// would ask for something the app never does.
    /// </summary>
    public static IReadOnlyList<Pipeline> Group(LiveConnection live, IReadOnlyList<Pipeline> pipelines, Pipeline sandbox)
    {
        var unit = live.Descriptor.FetchUnit;
        var key = PollGroup.KeyOf(unit, sandbox);
        return [..pipelines.Where(_ => PollGroup.KeyOf(unit, _) == key)];
    }

    /// <summary>
    /// Newest first, as the app orders a pipeline's runs.
    /// </summary>
    public static IReadOnlyList<Build> Newest(IEnumerable<Build> builds) =>
    [
        ..builds.OrderByDescending(_ => _.Ordering ?? DateTimeOffset.MinValue)
    ];

    static bool Same(string value, string wanted) =>
        string.Equals(value, wanted, StringComparison.OrdinalIgnoreCase);
}
