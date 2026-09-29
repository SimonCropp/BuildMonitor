/// <summary>
/// What passing builds share a group by, ignoring case: a prefix from
/// <see cref="Settings.GroupPrefixes"/> where the project starts with one, then the group the
/// service itself files the pipeline under, and otherwise the repository's short name, so one
/// project watched on two CI services reads as one group. With <see cref="Settings.GroupByOrg"/>
/// the owner of the repository, such as a GitHub organisation or an Azure DevOps project, comes
/// before the repository's name.
/// <para>
/// Only passes are grouped. A failure is what the window is for, and a group is a line that hides
/// its members: two broken pipelines of one repository read as "2 failing" and said neither which
/// broke nor what any of it links to. Passes are grouped for the opposite reason, that a
/// repository with a handful of green workflows otherwise buries the rows that need reading.
/// </para>
/// <para>
/// A prefix wins over the repository name because a family of repositories buries the red rows as
/// surely as one repository's workflows do: a dozen green pipelines across TheProjectApi,
/// TheProjectUI and TheProjectManager still took a row each until "TheProject" made them one. A
/// prefix wins over the service's own group too, since it is the one of the two someone typed.
/// </para>
/// <para>
/// The owner loses to both a prefix and the service's own group, since each of those names a
/// narrower family than everything one organisation owns.
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
    public static GroupKey? Of(Build build, ImmutableArray<string> prefixes, bool byOrg = false)
    {
        if (ProjectOf(build, prefixes, byOrg) is { } project)
        {
            return new(project);
        }

        return null;
    }

    /// <summary>
    /// What the key <see cref="Of"/> makes for the build is named, null where it makes none.
    /// </summary>
    static string? ProjectOf(Build build, ImmutableArray<string> prefixes, bool byOrg)
    {
        if (build.Status != BuildStatus.Succeeded)
        {
            return null;
        }

        var project = BuildExtensions.ShortRepoName(build.RepoName.AsSpan());
        if (Prefix(project, prefixes) is { } prefix)
        {
            return prefix;
        }

        if (build.ProjectGroup is { Length: > 0 } group)
        {
            return group;
        }

        if (byOrg &&
            Org(build) is { Length: > 0 } org)
        {
            return org.ToString();
        }

        return project.ToString();
    }

    /// <summary>
    /// The <see cref="Id"/> of the group the build joins, null where <see cref="Of"/> is, without
    /// making the key. Grouping needs every passing build's id but only a group's row needs a key,
    /// whose project is a string of its own.
    /// </summary>
    public static string? IdOf(Build build, ImmutableArray<string> prefixes, bool byOrg = false)
    {
        if (build.Status != BuildStatus.Succeeded)
        {
            return null;
        }

        var project = BuildExtensions.ShortRepoName(build.RepoName.AsSpan());
        if (Prefix(project, prefixes) is { } prefix)
        {
            return IdOf(prefix.AsSpan());
        }

        if (build.ProjectGroup is { Length: > 0 } group)
        {
            return IdOf(group.AsSpan());
        }

        if (byOrg &&
            Org(build) is { Length: > 0 } org)
        {
            return IdOf(org);
        }

        return IdOf(project);
    }

    /// <summary>
    /// The first segment of the repository's full name: the GitHub organisation or user, or the top
    /// GitLab group rather than a subgroup, so one owner is one group however deep its projects sit.
    /// Empty for a name with no owner in it. An Azure DevOps build's project, which its repository's
    /// name leaves out, stands in for the owner.
    /// </summary>
    static CharSpan Org(Build build)
    {
        if (build.Project is { Length: > 0 } project)
        {
            return project;
        }

        var repoName = build.RepoName.AsSpan();
        var slash = repoName.IndexOf('/');
        if (slash < 0)
        {
            return [];
        }

        return repoName[..slash];
    }

    /// <summary>
    /// The longest configured prefix the project starts with, or null. The longest, so a user who
    /// names both "TheProject" and "TheProjectManager" gets the narrower group they asked for
    /// rather than whichever they happened to type first.
    /// </summary>
    static string? Prefix(CharSpan project, ImmutableArray<string> prefixes)
    {
        string? found = null;
        foreach (var prefix in prefixes)
        {
            if (prefix.Length == 0 ||
                MatchedLength(project, prefix) < 0)
            {
                continue;
            }

            if (found is null ||
                prefix.Length > found.Length)
            {
                found = prefix;
            }
        }

        return found;
    }

    /// <summary>
    /// How much of the project the prefix covers, or -1 where the project does not start with it.
    /// Ignoring case, spaces and periods, so "Document Converter" and NServiceBus.Community.Validation
    /// fall under the DocumentConverter and NServiceBusCommunity someone typed, rather than only the
    /// spelling that matched it character for character.
    /// </summary>
    public static int MatchedLength(CharSpan project, CharSpan prefix)
    {
        var index = 0;
        foreach (var character in prefix)
        {
            if (character is ' ' or '.')
            {
                continue;
            }

            while (index < project.Length &&
                   project[index] is ' ' or '.')
            {
                index++;
            }

            if (index == project.Length ||
                char.ToLowerInvariant(project[index]) != char.ToLowerInvariant(character))
            {
                return -1;
            }

            index++;
        }

        return index;
    }

    /// <summary>
    /// Lower case and without spaces, so "Order Entry Service" on one service and
    /// OrderEntryService on another, one project named two ways, read as one group rather than two.
    /// </summary>
    static string IdOf(CharSpan project)
    {
        var lower = project.Length <= 256 ? stackalloc char[project.Length] : new char[project.Length];
        var length = 0;
        foreach (var character in project)
        {
            if (character == ' ')
            {
                continue;
            }

            lower[length] = char.ToLowerInvariant(character);
            length++;
        }

        return new(lower[..length]);
    }

    /// <summary>
    /// The key a group's row is named by, of the keys its members would each make. The longest,
    /// since members that differ only by spaces are one project and the spaced name reads better
    /// than the run together one; then the first alphabetically, so which name a group shows does not
    /// depend on which of its members built most recently.
    /// </summary>
    public static GroupKey Name(IEnumerable<GroupKey> keys) =>
        keys.OrderByDescending(_ => _.Project.Length)
            .ThenBy(_ => _.Project, StringComparer.Ordinal)
            .First();

    /// <summary>
    /// The key a group of <paramref name="members"/> is named by, as <see cref="Name(IEnumerable{GroupKey})"/>
    /// names it from the key of each. Only the one key is made: a key for every member, sorted to
    /// take the first, was four strings a passing build on every projection of the rows, and a
    /// repository's workflows, which follow each other, are all named alike.
    /// </summary>
    public static GroupKey Name(ImmutableArray<Build> members, ImmutableArray<string> prefixes, bool byOrg)
    {
        string? name = null;
        Build? previous = null;
        foreach (var member in members)
        {
            if (previous is not null &&
                member.RepoName == previous.RepoName &&
                member.ProjectGroup == previous.ProjectGroup &&
                member.Project == previous.Project)
            {
                continue;
            }

            previous = member;
            if (ProjectOf(member, prefixes, byOrg) is { } project &&
                (name is null || NamesBetter(project, name)))
            {
                name = project;
            }
        }

        return new(name!);
    }

    static bool NamesBetter(string project, string than) =>
        project.Length > than.Length ||
        (project.Length == than.Length && string.CompareOrdinal(project, than) < 0);
}
