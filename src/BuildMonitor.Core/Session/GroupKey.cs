/// <summary>
/// What passing builds share a group by: the repository's short name, ignoring case, so one
/// project watched on two CI services reads as one group.
/// <para>
/// Only passes are grouped. A failure is what the window is for, and a group is a line that hides
/// its members: two broken pipelines of one repository read as "2 failing" and said neither which
/// broke nor what any of it links to. Passes are grouped for the opposite reason, that a
/// repository with a handful of green workflows otherwise buries the rows that need reading.
/// </para>
/// </summary>
record GroupKey(string Project)
{
    // Get only, so a with expression can not change a key and leave its id behind.
    public string Project { get; } = Project;

    /// <summary>
    /// Built with the key rather than on each read. A projection of the rows reads it several times
    /// a group, and each read built a lower case copy of the project and then the id.
    /// </summary>
    public string Id { get; } = IdOf(Project);

    /// <summary>
    /// Null for a build that is never grouped: a failed one, which keeps the row of its own that
    /// says what broke; a running or queued one, which keeps a row of its own at the top; and a
    /// cancelled one, which says nothing about the project.
    /// </summary>
    public static GroupKey? Of(Build build)
    {
        if (build.Status == BuildStatus.Succeeded)
        {
            return new(build.ShortRepoName());
        }

        return null;
    }

    /// <summary>
    /// The <see cref="Id"/> of the group the build joins, null where <see cref="Of"/> is, without
    /// making the key. Grouping needs every passing build's id but only a group's row needs a key,
    /// whose project is a string of its own.
    /// </summary>
    public static string? IdOf(Build build)
    {
        if (build.Status == BuildStatus.Succeeded)
        {
            return IdOf(BuildExtensions.ShortRepoName(build.RepoName.AsSpan()));
        }

        return null;
    }

    static string IdOf(CharSpan project)
    {
        var lower = project.Length <= 256 ? stackalloc char[project.Length] : new char[project.Length];
        project.ToLowerInvariant(lower);
        return new(lower);
    }
}
