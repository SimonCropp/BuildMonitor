/// <summary>
/// The watcher is the one part of the feature that touches the disk while the app runs, so these
/// go through a real directory rather than a double.
/// </summary>
public class LocalRepoWatcherTests
{
    static readonly TimeSpan settle = TimeSpan.FromMilliseconds(20);

    [Test]
    public async Task ScansWhatIsThereWhenItIsPointedAtADirectory()
    {
        var root = TempDirectory();
        try
        {
            Repo(root, "DiffEngine", "https://github.com/VerifyTests/DiffEngine.git");
            var host = new SessionHost(SessionState.Start(new()));
            using var watcher = new LocalRepoWatcher(host, settle);

            watcher.Sync(root);

            var repos = await Until(host, _ => _.Count > 0);
            await Assert.That(LocalRepos.Find(repos, "VerifyTests/DiffEngine")).IsEqualTo(Path.Combine(root, "DiffEngine"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// The second checkout can only have been found by watching: the scan that ran when Sync was
    /// called could not have seen it.
    /// </summary>
    [Test]
    public async Task NoticesACheckoutThatAppearsAfterwards()
    {
        var root = TempDirectory();
        try
        {
            Repo(root, "DiffEngine");
            var host = new SessionHost(SessionState.Start(new()));
            using var watcher = new LocalRepoWatcher(host, settle);
            watcher.Sync(root);
            await Until(host, _ => _.Count == 1);

            Repo(root, "Verify");

            var repos = await Until(host, _ => _.Count == 2);
            await Assert.That(LocalRepos.Find(repos, "Verify")).IsEqualTo(Path.Combine(root, "Verify"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task PointingItAtNothingClearsWhatItFound()
    {
        var root = TempDirectory();
        try
        {
            Repo(root, "DiffEngine");
            var host = new SessionHost(SessionState.Start(new()));
            using var watcher = new LocalRepoWatcher(host, settle);
            watcher.Sync(root);
            await Until(host, _ => _.Count > 0);

            watcher.Sync("");

            await Until(host, _ => _.Count == 0);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// A path the user typed and has not created yet is ordinary, and must not take the tray down
    /// with it.
    /// </summary>
    [Test]
    public async Task ADirectoryThatIsNotThereFindsNothing()
    {
        var host = new SessionHost(SessionState.Start(new()));
        using var watcher = new LocalRepoWatcher(host, settle);

        watcher.Sync(Path.Combine(Path.GetTempPath(), $"BuildMonitorGone_{Guid.NewGuid():N}"));
        await Task.Delay(200);

        await Assert.That(host.State.LocalRepos).IsEmpty();
    }

    /// <summary>
    /// Polls rather than waits on a signal: the scan lands from the thread pool, which is how
    /// every other background part of the app reports back.
    /// </summary>
    static async Task<ImmutableDictionary<string, string>> Until(SessionHost host, Func<ImmutableDictionary<string, string>, bool> wanted)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            var repos = host.State.LocalRepos;
            if (wanted(repos))
            {
                return repos;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new($"The watcher never reported what was wanted; it last held {repos.Count} repositories");
            }

            await Task.Delay(20);
        }
    }

    static void Repo(string root, string name, string? origin = null)
    {
        var directory = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(directory, ".git"));
        if (origin is not null)
        {
            File.WriteAllText(
                Path.Combine(directory, ".git", "config"),
                $"[remote \"origin\"]\n\turl = {origin}\n");
        }
    }

    static string TempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"BuildMonitorWatcher_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
