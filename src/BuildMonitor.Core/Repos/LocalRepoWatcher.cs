/// <summary>
/// Keeps <see cref="SessionState.LocalRepos"/> current while the app runs: scans the code directory
/// once at startup and again whenever a folder appears or disappears near the top of it, so a fresh
/// clone gets its open folder button without a restart.
/// <para>
/// Watches the root and the directories above the depth a checkout can be found at, each one on its
/// own non-recursive watcher. One recursive watcher would be less code, but on Linux that registers
/// an inotify watch per directory in the whole tree: a code directory holding fifty repositories and
/// their node_modules would exhaust max_user_watches and then stop reporting anything, silently.
/// The bounded set is one watch per directory that could hold a checkout, and no more.
/// </para>
/// </summary>
sealed class LocalRepoWatcher : IDisposable
{
    SessionHost host;
    TimeSpan settle;
    Lock gate = new();
    List<FileSystemWatcher> watchers = [];
    Timer debounce;
    string? root;
    bool disposed;

    /// <summary>
    /// A clone writes a directory and then its .git, and a checkout writes many folders in a burst.
    /// Rescanning on each would walk the tree dozens of times for one clone, so events settle for
    /// this long first.
    /// </summary>
    static TimeSpan defaultSettle = TimeSpan.FromMilliseconds(750);

    /// <param name="settle">Shortened by tests, which would otherwise wait out the debounce of
    /// every scan they trigger.</param>
    public LocalRepoWatcher(SessionHost host, TimeSpan? settle = null)
    {
        this.host = host;
        this.settle = settle ?? defaultSettle;
        debounce = new(_ => Rescan(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Points the watcher at <paramref name="directory"/>, or at nothing when it is empty. Safe to
    /// call with the directory it already watches, which is what every save of the options page
    /// does.
    /// </summary>
    public void Sync(string? directory)
    {
        var wanted = string.IsNullOrWhiteSpace(directory) ? null : directory.Trim();
        lock (gate)
        {
            if (disposed ||
                string.Equals(wanted, root, StringComparison.Ordinal))
            {
                return;
            }

            root = wanted;
        }

        Schedule();
    }

    void Schedule()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            debounce.Change(settle, Timeout.InfiniteTimeSpan);
        }
    }

    void Rescan()
    {
        string? scanning;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            scanning = root;
        }

        try
        {
            var repos = LocalRepos.Scan(scanning);
            var index = LocalRepos.Index(repos);
            Rearm(scanning);
            host.Mutate(_ => MonitorSession.ApplyLocalRepos(_, index));
            Log.Debug("Found {Count} local repositories under {Root}", repos.Length, scanning ?? "nothing");
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Scanning {Root} for local repositories failed", scanning ?? "nothing");
        }
    }

    /// <summary>
    /// One watcher on the root and one on every directory that could still hold a checkout, which
    /// is every directory walked past rather than into. A directory that is already a checkout is
    /// not watched: what changes inside it is the user's work, not a new repository.
    /// </summary>
    void Rearm(string? scanning)
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            foreach (var watcher in watchers)
            {
                Close(watcher);
            }

            watchers.Clear();
            if (scanning is null ||
                !Directory.Exists(scanning))
            {
                return;
            }

            foreach (var directory in Watchable(scanning))
            {
                if (Watch(directory) is { } watcher)
                {
                    watchers.Add(watcher);
                }
            }
        }
    }

    /// <summary>
    /// The root, then every directory down to the depth a checkout can be found at. A checkout is
    /// watched, so that removing its .git takes its button away, but is not descended into: what
    /// changes below it is the user's work, and one watch per folder of every repository is the
    /// recursive cost this class exists to avoid.
    /// </summary>
    static IEnumerable<string> Watchable(string root)
    {
        var directories = new List<string> { root };
        var level = new List<string> { root };
        for (var depth = 1; depth <= LocalRepos.MaxDepth; depth++)
        {
            var next = new List<string>();
            foreach (var parent in level)
            {
                foreach (var child in LocalRepos.Children(parent))
                {
                    directories.Add(child);
                    if (depth < LocalRepos.MaxDepth &&
                        !LocalRepos.IsRepo(child))
                    {
                        next.Add(child);
                    }
                }
            }

            level = next;
        }

        return directories;
    }

    FileSystemWatcher? Watch(string directory)
    {
        try
        {
            var watcher = new FileSystemWatcher(directory)
            {
                // A repository announces itself by its .git, and a folder appearing is what says
                // there may be one to look for one level down.
                NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
                IncludeSubdirectories = false
            };
            watcher.Created += (_, _) => Schedule();
            watcher.Deleted += (_, _) => Schedule();
            watcher.Renamed += (_, _) => Schedule();
            // The buffer overflows when a great many changes land at once, and every event since
            // the last read is lost with it. A full rescan is the only way back to the truth.
            watcher.Error += (_, _) => Schedule();
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception exception)
        {
            Log.Debug(exception, "Could not watch {Directory}", directory);
            return null;
        }
    }

    static void Close(FileSystemWatcher watcher)
    {
        try
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        catch (Exception exception)
        {
            Log.Debug(exception, "Could not stop a directory watcher");
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            debounce.Dispose();
            foreach (var watcher in watchers)
            {
                Close(watcher);
            }

            watchers.Clear();
        }
    }
}
