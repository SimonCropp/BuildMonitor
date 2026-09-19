/// <summary>
/// What finished builds share a group by: the repository's short name, ignoring case, so one
/// project watched on two CI services reads as one group; and whether they failed, so a collapsed
/// group is one colour and can never hide a failure behind a pass.
/// </summary>
record GroupKey(string Project, bool Failed)
{
    // Get only, so a with expression can not change a key and leave its id behind.
    public string Project { get; } = Project;

    public bool Failed { get; } = Failed;

    /// <summary>
    /// Built with the key rather than on each read. A projection of the rows reads it several times
    /// a group, and each read built a lower case copy of the project and then the id.
    /// </summary>
    public string Id { get; } = IdOf(Project, Failed);

    /// <summary>
    /// Null for a build that is never grouped: a running or queued one keeps a row of its own at
    /// the top, and a cancelled one says nothing about the project.
    /// </summary>
    public static GroupKey? Of(Build build)
    {
        if (FailedOf(build) is { } failed)
        {
            return new(build.ShortRepoName(), failed);
        }

        return null;
    }

    /// <summary>
    /// The <see cref="Id"/> of the group the build joins, null where <see cref="Of"/> is, without
    /// making the key. Grouping needs every finished build's id but only a group's row needs a key,
    /// whose project is a string of its own.
    /// </summary>
    public static string? IdOf(Build build)
    {
        if (FailedOf(build) is { } failed)
        {
            return IdOf(BuildExtensions.ShortRepoName(build.RepoName.AsSpan()), failed);
        }

        return null;
    }

    static bool? FailedOf(Build build) =>
        build.Status switch
        {
            BuildStatus.Failed => true,
            BuildStatus.Succeeded => false,
            _ => null
        };

    static string IdOf(CharSpan project, bool failed)
    {
        var lower = project.Length <= 256 ? stackalloc char[project.Length] : new char[project.Length];
        project.ToLowerInvariant(lower);
        var outcome = failed ? "failed/" : "passed/";
        return string.Concat(outcome, lower);
    }
}
