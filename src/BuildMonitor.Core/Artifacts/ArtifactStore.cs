/// <summary>
/// The directory one build's artifacts and log are downloaded into, and the sweep that takes them
/// away again.
/// <para>
/// A bundle is written to a staging directory and moved into place, the way
/// <see cref="SettingsHelper"/> writes settings: a directory at its final path is always a complete
/// one, so a prompt composed the moment it appears cannot name a half downloaded artifact.
/// </para>
/// </summary>
sealed class ArtifactStore(Func<DateTimeOffset>? clock = null)
{
    /// <summary>
    /// How long a bundle is kept. Long enough that a triage started before lunch still has its
    /// files after it, short enough that a tray left running for weeks is not holding every
    /// artifact of every failure it was ever asked about.
    /// </summary>
    public static TimeSpan Retention = TimeSpan.FromHours(24);

    /// <summary>
    /// How much of a readable name is kept before the hash. A branch, or an Octopus project, runs
    /// longer than a path has room for once the root and the file names inside it are added.
    /// </summary>
    const int maxStem = 40;

    /// <summary>
    /// The staging directories being written right now, which <see cref="Sweep"/> leaves alone
    /// however old they look. A download of one large artifact can hold a directory open for
    /// minutes, and sweeping it would delete files out from under the writer.
    /// </summary>
    ConcurrentDictionary<string, byte> writing = new();

    /// <summary>
    /// Where <paramref name="build"/>'s files go. Stable for one run, so a second triage of it
    /// reuses the name and replaces what is there rather than growing a second copy, and keyed on
    /// the run number as well as the row's key, so a retry's artifacts never arrive under the name
    /// of the run before it.
    /// </summary>
    public static string DirectoryFor(Build build) =>
        Path.Combine(AppPaths.Artifacts, $"{Stem(build)}-{Hash(build)}");

    /// <summary>
    /// Runs <paramref name="write"/> against a staging directory and moves the result into place,
    /// returning where it landed. Anything already at the final path is deleted first: a second
    /// triage of one run must replace what it finds rather than mix new files among the last
    /// attempt's, which would hand an assistant a file list that never existed on the CI service.
    /// </summary>
    public async Task<string> Write(Build build, Func<string, Task> write)
    {
        var final = DirectoryFor(build);
        // Named uniquely rather than "{final}.partial": two clicks on one row race, and a shared
        // staging name would have the second delete the first one's files mid download.
        var partial = $"{final}.{Guid.NewGuid():N}.partial";
        Directory.CreateDirectory(partial);
        writing[partial] = 0;
        try
        {
            await write(partial);
            Delete(final);
            Directory.Move(partial, final);
            // Set rather than inherited, so the age the sweep reads is when the bundle was finished,
            // whatever a platform does to a directory's time as files land in it, and so a second
            // triage of one run pushes its files back out of the sweep's reach.
            Directory.SetLastWriteTimeUtc(final, Now().UtcDateTime);
            return final;
        }
        catch
        {
            Delete(partial);
            throw;
        }
        finally
        {
            writing.TryRemove(partial, out _);
        }
    }

    /// <summary>
    /// Deletes every bundle past <see cref="Retention"/>, and the staging directories a run killed
    /// mid download left behind. One that will not delete, because a file manager or a scanner
    /// holds a handle on it, is logged and stepped over: the rest are still worth taking.
    /// </summary>
    public void Sweep()
    {
        var root = AppPaths.Artifacts;
        if (!Directory.Exists(root))
        {
            return;
        }

        var cutoff = Now().UtcDateTime - Retention;
        foreach (var directory in Children(root))
        {
            if (writing.ContainsKey(directory) ||
                Directory.GetLastWriteTimeUtc(directory) > cutoff)
            {
                continue;
            }

            Delete(directory);
        }
    }

    /// <summary>
    /// A name a filesystem will take. Anything outside letters, digits, dot, dash and underscore
    /// becomes a dash, and runs of dashes collapse: a build key holds slashes, so does a branch,
    /// and Windows refuses a name ending in a dot or a space, so none of them can reach a path as
    /// they stand.
    /// <para>
    /// Also what names the files inside a bundle, because several services report an artifact's
    /// path rather than its name, and a <c>..</c> in one would otherwise write outside the bundle.
    /// </para>
    /// </summary>
    public static string Safe(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (char.IsAsciiLetterOrDigit(character) ||
                character is '.' or '-' or '_')
            {
                builder.Append(character);
                continue;
            }

            if (builder.Length > 0 &&
                builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var safe = builder.ToString().Trim('-', '.');
        if (safe.Length == 0)
        {
            return "file";
        }

        return safe;
    }

    /// <summary>
    /// A readable stem and a hash of the key. The stem alone collides: one repository failing on
    /// two branches, or two connections watching one repository, shorten to the same text. The hash
    /// alone is unreadable, and these paths go into a prompt and into the user's own file manager,
    /// where a name nobody can tie back to a row is a name nobody dares delete.
    /// </summary>
    static string Stem(Build build)
    {
        var stem = Safe($"{build.ShortRepoName()}-{build.RunNumberLabel()}");
        if (stem.Length > maxStem)
        {
            return stem[..maxStem].TrimEnd('-', '.');
        }

        return stem;
    }

    /// <summary>
    /// Over the run as well as the row, because a row's key is the same for every run on its branch
    /// and a retry must not land on the previous run's files. Always follows the stem, so a
    /// repository called <c>con</c> or <c>aux</c> cannot land on a reserved device name either.
    /// </summary>
    static string Hash(Build build) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{build.Key}\n{build.RunNumber}")))[..8];

    /// <summary>
    /// The subdirectories of the root, an unreadable one treated as none the way
    /// <see cref="LocalRepos"/> treats a directory it cannot list: one that refuses should not stop
    /// the rest being swept.
    /// </summary>
    static IReadOnlyList<string> Children(string root)
    {
        try
        {
            return Directory.GetDirectories(root);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Could not list {Directory}", root);
            return [];
        }
    }

    static void Delete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Could not delete {Directory}", directory);
        }
    }

    DateTimeOffset Now() =>
        clock?.Invoke() ?? DateTimeOffset.UtcNow;
}
