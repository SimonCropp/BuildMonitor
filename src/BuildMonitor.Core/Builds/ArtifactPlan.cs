/// <summary>
/// Which of a build's artifacts are worth downloading for a triage, and what to say about the rest.
/// <para>
/// Pure, so it verifies as text rather than against a service, the way the screen and the triage
/// prompt do. Nothing here touches HTTP, the filesystem or a clock: the ranking is a rule about
/// names and sizes, and a rule is easier to argue with when it is written down on its own.
/// </para>
/// </summary>
record ArtifactPlan(ImmutableArray<BuildArtifact> Take, ImmutableArray<SkippedArtifact> Skip)
{
    /// <summary>
    /// How much one triage downloads in total. The consumer is an assistant reading files, and
    /// everything that actually explains a failure — a test report, a junit file, a log, an
    /// approval diff, a screenshot — is kilobytes to a few megabytes. This takes all of those and a
    /// couple of medium archives, in seconds to tens of seconds, without the tray noticing.
    /// </summary>
    public const long DefaultBudget = 50 * 1024 * 1024;

    /// <summary>
    /// The most one file may take. An artifact larger than this is nearly always a build output —
    /// an installer, a container layer, a published binary — and reading it explains nothing, while
    /// downloading it would spend the whole budget on one file.
    /// </summary>
    public const long DefaultPerFile = 20 * 1024 * 1024;

    /// <summary>
    /// How many files at most. A wide matrix uploads one artifact per leg, and the twenty first
    /// says nothing the first twenty did not.
    /// </summary>
    public const int DefaultCount = 20;

    /// <summary>
    /// The artifacts in the order they are worth reading, most useful first.
    /// <para>
    /// Ranked into bands by name, then smallest first inside a band, then by name so the order is
    /// stable whatever order the service listed them in. Smallest first because more distinct
    /// artifacts fit the budget that way, and because the small file is far more often the report
    /// while the large one is the thing the report describes.
    /// </para>
    /// </summary>
    public static ImmutableArray<BuildArtifact> Order(IEnumerable<BuildArtifact> artifacts) =>
        [
            ..artifacts
                .OrderBy(Band)
                // An unknown size cannot be weighed, so it goes after the sizes that can be, rather
                // than sorting as though it were zero and taking the front of its band.
                .ThenBy(_ => _.Bytes is null)
                .ThenBy(_ => _.Bytes ?? 0)
                .ThenBy(_ => _.Name, StringComparer.OrdinalIgnoreCase)
        ];

    /// <summary>
    /// What to download and what to report as left behind, spending
    /// <paramref name="budget"/> over <see cref="Order"/>.
    /// <para>
    /// An artifact whose size the service never gave is charged as nothing and taken under the per
    /// file cap, because the alternative is either skipping every Jenkins artifact or guessing a
    /// size. It is found out while copying instead, and the caller adds the skip then.
    /// </para>
    /// </summary>
    public static ArtifactPlan For(
        IEnumerable<BuildArtifact> artifacts,
        long budget = DefaultBudget,
        long perFile = DefaultPerFile,
        int count = DefaultCount)
    {
        var take = ImmutableArray.CreateBuilder<BuildArtifact>();
        var skip = ImmutableArray.CreateBuilder<SkippedArtifact>();
        var left = budget;
        foreach (var artifact in Order(artifacts))
        {
            if (artifact.Unavailable is { Length: > 0 } unavailable)
            {
                skip.Add(new(artifact.Name, artifact.Bytes, unavailable));
                continue;
            }

            if (take.Count == count)
            {
                skip.Add(new(artifact.Name, artifact.Bytes, $"the {count} file limit was reached"));
                continue;
            }

            if (artifact.Bytes is { } bytes)
            {
                if (bytes > perFile)
                {
                    skip.Add(new(artifact.Name, bytes, $"{ByteSize.Human(bytes)}, over the {ByteSize.Human(perFile)} limit for one file"));
                    continue;
                }

                if (bytes > left)
                {
                    skip.Add(new(artifact.Name, bytes, $"{ByteSize.Human(bytes)}, and only {ByteSize.Human(left)} of the {ByteSize.Human(budget)} budget was left"));
                    continue;
                }

                left -= bytes;
            }

            take.Add(artifact);
        }

        return new(take.ToImmutable(), skip.ToImmutable());
    }

    /// <summary>
    /// The most this artifact may take, for the call that fetches it: whatever is left of the
    /// budget, never more than one file's share. The only guard for an artifact whose size the
    /// service did not declare.
    /// </summary>
    public static long Limit(long left, long perFile = DefaultPerFile) =>
        Math.Min(perFile, Math.Max(left, 0));

    /// <summary>
    /// Which band a name falls in, lowest read first. Matched on the name the service gave, which
    /// may be a path, so every test is a contains or an extension rather than an exact name.
    /// </summary>
    static int Band(BuildArtifact artifact)
    {
        var name = artifact.Name.ToLowerInvariant();
        // A bare .zip is deliberately not demoted: every GitHub artifact arrives as one, whatever
        // was uploaded, so a rule against the extension would push a whole service to the back.
        if (Holds(name, "test-result", "testresult", "junit", "nunit", "xunit", ".trx", ".tap"))
        {
            return 1;
        }

        // Before the log band, which would otherwise take every approval file: the ones that most
        // often explain a failure are named ".received.txt" and ".verified.txt".
        if (Holds(name, ".received.", ".verified.") ||
            Ends(name, ".diff", ".patch"))
        {
            return 3;
        }

        if (Ends(name, ".log", ".txt", ".binlog") ||
            Holds(name, "diag", "crash", "dump"))
        {
            return 2;
        }

        if (Ends(name, ".png", ".jpg", ".jpeg", ".har", ".trace", ".webm", ".mp4") ||
            Holds(name, "screenshot", "playwright-report"))
        {
            return 4;
        }

        if (Holds(name, "coverage", "cobertura", "opencover"))
        {
            return 5;
        }

        if (Ends(name, ".nupkg", ".snupkg", ".exe", ".dll", ".msi", ".pkg", ".dmg", ".deb", ".rpm", ".iso", ".tar", ".tar.gz", ".tgz", ".apk", ".aab", ".jar", ".war"))
        {
            return 7;
        }

        return 6;
    }

    static bool Holds(string name, params string[] needles) =>
        needles.Any(_ => name.Contains(_, StringComparison.Ordinal));

    static bool Ends(string name, params string[] suffixes) =>
        suffixes.Any(_ => name.EndsWith(_, StringComparison.Ordinal));
}
