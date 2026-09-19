/// <summary>
/// Fills one bundle: the build's log as <c>log.txt</c>, then as many of its artifacts as the
/// budget reaches, and a note of everything left behind.
/// <para>
/// The ranking and the budget are <see cref="ArtifactPlan"/>'s, and the directory's lifetime is
/// <see cref="ArtifactStore"/>'s. What is left here is the part that cannot be pure: fetching,
/// naming files so two artifacts cannot overwrite each other, and re-planning as the budget is
/// actually spent.
/// </para>
/// </summary>
static class ArtifactCollector
{
    /// <summary>
    /// The build's whole log, not the tail the protocol serves. An assistant starting cold reads
    /// this from disk instead of spending a call on the end of each section.
    /// </summary>
    public const string LogName = "log.txt";

    public static async Task<TriageFilesDto> Collect(ArtifactStore store, Poller poller, Build build, Cancel cancel)
    {
        var log = await Log(poller, build, cancel);
        var descriptor = poller.Descriptor(build);
        var listed = descriptor.HasArtifacts
            ? await poller.WithProvider(build, (provider, context) => provider.ListArtifacts(context, build, cancel))
            : [];
        var plan = ArtifactPlan.For(listed);
        // Nothing to write, so no empty directory is left for the sweep to find later.
        if (log.Length == 0 &&
            plan.Take.Length == 0)
        {
            return new("", [], Skipped(plan.Skip), Unsupported(descriptor));
        }

        var files = new List<string>();
        var skipped = plan.Skip.ToList();
        var directory = await store.Write(build, async staging =>
        {
            if (log.Length > 0)
            {
                await File.WriteAllTextAsync(Path.Combine(staging, LogName), log, cancel);
                files.Add(LogName);
            }

            await Download(poller, build, plan, staging, files, skipped, cancel);
        });

        return new(directory, files, Skipped(skipped), Unsupported(descriptor));
    }

    static Task Download(Poller poller, Build build, ArtifactPlan plan, string staging, List<string> files, List<SkippedArtifact> skipped, Cancel cancel)
    {
        if (plan.Take.Length == 0)
        {
            return Task.CompletedTask;
        }

        // log.txt is claimed before any artifact is named, so a build that published a file of that
        // name gets log-2.txt rather than quietly overwriting the build's own log.
        var taken = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        var left = ArtifactPlan.DefaultBudget;
        return poller.WithProvider(build, async (provider, context) =>
        {
            foreach (var artifact in plan.Take)
            {
                var name = Unique(ArtifactStore.Safe(artifact.Name), taken);
                var path = Path.Combine(staging, name);
                long written;
                try
                {
                    await using var destination = File.Create(path);
                    written = await provider.DownloadArtifact(context, build, artifact, destination, ArtifactPlan.Limit(left), cancel);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // The part file is removed and the artifact reported, rather than the whole
                    // bundle failing: one artifact that will not come is worth saying, and the ones
                    // that did come are still worth having.
                    Delete(path);
                    taken.Remove(name);
                    skipped.Add(new(artifact.Name, artifact.Bytes, Reason(exception)));
                    continue;
                }

                files.Add(name);
                left -= written;
            }
        });
    }

    /// <summary>
    /// What went wrong, short enough for a line of a prompt. An artifact over the cap says so in
    /// the same words the plan uses for one it could weigh in advance.
    /// </summary>
    static string Reason(Exception exception)
    {
        if (exception is ArtifactTooLargeException)
        {
            return exception.Message.ToLowerInvariant();
        }

        return $"the download failed: {exception.Message}";
    }

    /// <summary>
    /// The log, or empty where the build has none, as a run that failed before it started a job
    /// does. A log that cannot be fetched is not a failed collect: the artifacts are still worth
    /// having, and the prompt says the log is missing.
    /// </summary>
    static async Task<string> Log(Poller poller, Build build, Cancel cancel)
    {
        try
        {
            return await poller.FetchLog(build, cancel);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Serilog.Log.Warning(exception, "Fetching the log of {Build} for triage failed", build.PipelineName);
            return "";
        }
    }

    static string? Unsupported(ProviderDescriptor descriptor)
    {
        if (descriptor.HasArtifacts)
        {
            return null;
        }

        return $"BuildMonitor cannot list artifacts for {descriptor.Name}";
    }

    /// <summary>
    /// A name nothing in the bundle has yet. Two jobs archiving a results file each call it the
    /// same thing, and the second would otherwise replace the first without anything saying so.
    /// </summary>
    static string Unique(string name, HashSet<string> taken)
    {
        if (taken.Add(name))
        {
            return name;
        }

        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var index = 2; ; index++)
        {
            var candidate = $"{stem}-{index}{extension}";
            if (taken.Add(candidate))
            {
                return candidate;
            }
        }
    }

    static IReadOnlyList<SkippedArtifactDto> Skipped(IEnumerable<SkippedArtifact> skipped) =>
        skipped.Select(_ => new SkippedArtifactDto(_.Name, _.Bytes, _.Reason)).ToList();

    static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception)
        {
            Serilog.Log.Warning(exception, "Could not delete {File}", path);
        }
    }
}
