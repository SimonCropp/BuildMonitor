/// <summary>
/// What finished builds share a group by: the repository's short name, ignoring case, so one
/// project watched on two CI services reads as one group; and whether they failed, so a collapsed
/// group is one colour and can never hide a failure behind a pass.
/// </summary>
record GroupKey(string Project, bool Failed)
{
    public string Id => $"{(Failed ? "failed" : "passed")}/{Project.ToLowerInvariant()}";

    /// <summary>
    /// Null for a build that is never grouped: a running or queued one keeps a row of its own at
    /// the top, and a cancelled one says nothing about the project.
    /// </summary>
    public static GroupKey? Of(Build build) =>
        build.Status switch
        {
            BuildStatus.Failed => new(build.ShortRepoName(), true),
            BuildStatus.Succeeded => new(build.ShortRepoName(), false),
            _ => null
        };
}
