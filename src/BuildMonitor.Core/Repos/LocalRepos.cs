/// <summary>
/// Finds the git checkouts under the code directory and matches them to the builds being watched.
/// <para>
/// <see cref="Find"/> is the one rule: the chip and the click both go through it, so a row cannot
/// offer an open folder button that then opens nothing.
/// </para>
/// </summary>
static class LocalRepos
{
    /// <summary>
    /// How far below the root a checkout is looked for. One level holds the flat case, two the
    /// common one of a folder per organisation. Deeper would walk into the checkouts themselves,
    /// where every submodule and vendored dependency is another .git.
    /// </summary>
    public const int MaxDepth = 2;

    /// <summary>
    /// Every checkout at most <see cref="MaxDepth"/> folders below <paramref name="root"/>. A
    /// directory that is itself a checkout is not descended into, so a submodule never shadows the
    /// repository holding it. A root that is missing or unreadable scans to nothing rather than
    /// throwing: it is a path the user typed.
    /// </summary>
    public static ImmutableArray<LocalRepo> Scan(string? root)
    {
        if (string.IsNullOrWhiteSpace(root) ||
            !Directory.Exists(root))
        {
            return [];
        }

        var found = ImmutableArray.CreateBuilder<LocalRepo>();
        Walk(root, 1, found);
        return found.ToImmutable();
    }

    static void Walk(string directory, int depth, ImmutableArray<LocalRepo>.Builder found)
    {
        foreach (var child in Children(directory))
        {
            if (IsRepo(child))
            {
                found.Add(new(child, Path.GetFileName(child), GitRemote.Of(child)));
                continue;
            }

            if (depth < MaxDepth)
            {
                Walk(child, depth + 1, found);
            }
        }
    }

    /// <summary>
    /// The subdirectories worth looking at, hidden ones left out so a scan does not walk into
    /// .vs, .idea or a trash folder. Unreadable is treated as empty: one directory the user cannot
    /// open should not stop the rest being found.
    /// </summary>
    public static IReadOnlyList<string> Children(string directory)
    {
        try
        {
            return new DirectoryInfo(directory)
                .EnumerateDirectories()
                .Where(_ => !_.Attributes.HasFlag(FileAttributes.Hidden) &&
                            !_.Name.StartsWith('.'))
                .Select(_ => _.FullName)
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Debug(exception, "Could not list {Directory}", directory);
            return [];
        }
    }

    /// <summary>
    /// A checkout has a .git, which is a directory ordinarily and a file in a worktree or a
    /// submodule.
    /// </summary>
    public static bool IsRepo(string directory)
    {
        var git = Path.Combine(directory, ".git");
        return Directory.Exists(git) || File.Exists(git);
    }

    /// <summary>
    /// The checkouts keyed by everything a build might name them: the origin's path, which is what
    /// GitHub, GitLab, Travis and Bitbucket report, and the folder's own name, which is the only
    /// thing that can match TeamCity, Octopus, GoCd or Jenkins, whose repository name is a project
    /// rather than a slug.
    /// <para>
    /// Keys are lower cased, and a folder name never displaces a remote: two checkouts of the same
    /// name under different organisations would otherwise have the second silently win the row of
    /// the first.
    /// </para>
    /// </summary>
    public static ImmutableDictionary<string, string> Index(IEnumerable<LocalRepo> repos)
    {
        var all = repos.ToList();
        var index = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var repo in all.Where(_ => _.Remote is not null))
        {
            index[repo.Remote!] = repo.Directory;
        }

        foreach (var repo in all)
        {
            if (!index.ContainsKey(repo.Name))
            {
                index[repo.Name] = repo.Directory;
            }
        }

        return index.ToImmutable();
    }

    /// <summary>
    /// Where <paramref name="build"/>'s repository is checked out, or null. The whole repository
    /// name first, so "SimonCropp/BuildMonitor" beats a folder that happens to be called
    /// BuildMonitor, then its last segment for the providers that report no owner.
    /// </summary>
    public static string? Find(ImmutableDictionary<string, string> index, Build build) =>
        Find(index, build.RepoName);

    /// <summary>
    /// The one checkout every one of <paramref name="builds"/> resolves to, or null when they
    /// resolve to different ones or any of them to none. A group's row stands for its members, so
    /// it can only offer the folder all of them agree on.
    /// </summary>
    public static string? Shared(ImmutableDictionary<string, string> index, ImmutableArray<Build> builds)
    {
        string? shared = null;
        foreach (var build in builds)
        {
            if (Find(index, build) is not { } directory)
            {
                return null;
            }

            if (shared is null)
            {
                shared = directory;
                continue;
            }

            if (!string.Equals(shared, directory, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return shared;
    }

    public static string? Find(ImmutableDictionary<string, string> index, string repoName)
    {
        if (index.Count == 0 ||
            repoName.Length == 0)
        {
            return null;
        }

        if (index.TryGetValue(repoName, out var directory))
        {
            return directory;
        }

        var last = repoName[(repoName.LastIndexOf('/') + 1)..];
        if (last.Length > 0 &&
            index.TryGetValue(last, out var byName))
        {
            return byName;
        }

        return null;
    }
}
