/// <summary>
/// What the live tests print. The repository is public, so a workflow's log is too.
/// <see cref="Line"/> carries only counts, statuses, run numbers and configured values.
/// <see cref="Detail"/> names what discovery found, and prints only outside GitHub Actions.
/// Otherwise a token that reaches more than its sandbox would list a whole account in a public log.
/// </summary>
static class LiveLog
{
    /// <summary>
    /// The process's own output. TUnit captures a test's console, and a workflow command only takes
    /// effect when it reaches the runner's log.
    /// </summary>
    static Lazy<TextWriter> output = new(() => TextWriter.Synchronized(new StreamWriter(Console.OpenStandardOutput())
    {
        AutoFlush = true
    }));

    public static void Line(string text) =>
        Console.WriteLine(text);

    public static void Detail(string text)
    {
        if (!LiveSettings.Public)
        {
            Console.WriteLine(text);
        }
    }

    /// <summary>
    /// Something that does not fail the run but should be seen. In a workflow it is also an
    /// annotation on the run's page.
    /// </summary>
    public static void Warning(string text)
    {
        Console.WriteLine($"warning: {text}");
        if (LiveSettings.Public)
        {
            output.Value.WriteLine($"::warning::{text}");
        }
    }

    public static string Row(Build build)
    {
        var text = $"{build.RunNumberLabel()} {build.Status}".Trim();
        if (build.Retryable())
        {
            text += " retryable";
        }

        if (build.CanCancel)
        {
            text += " cancellable";
        }

        return text;
    }

    /// <summary>
    /// The newest few builds, as a poll reports what it is waiting on.
    /// </summary>
    public static string Rows(IReadOnlyList<Build> builds)
    {
        if (builds.Count == 0)
        {
            return "no builds";
        }

        return string.Join(", ", builds.Take(4).Select(Row));
    }

    /// <summary>
    /// What a build published, for a failure that expected one of them by name.
    /// </summary>
    public static string Names(IReadOnlyList<BuildArtifact> artifacts)
    {
        if (artifacts.Count == 0)
        {
            return "no files at all";
        }

        return string.Join(", ", artifacts.Take(10).Select(_ => $"'{_.Name}'"));
    }

    public static string Counts(IEnumerable<Build> builds)
    {
        var counts = builds
            .GroupBy(_ => _.Status)
            .OrderBy(_ => _.Key)
            .Select(_ => $"{_.Count()} {_.Key}")
            .ToList();
        if (counts.Count == 0)
        {
            return "none";
        }

        return string.Join(", ", counts);
    }
}
