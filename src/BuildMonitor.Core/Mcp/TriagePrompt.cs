/// <summary>
/// The text the triage command hands an assistant: the builds failing now, where each one is
/// checked out, and the order to work through them in.
/// <para>
/// Pure, so it verifies as text rather than through an MCP client, the way the screen does.
/// </para>
/// <para>
/// Builds are gathered under the pipeline they share as a hint, and the text says it is one:
/// several repositories red on one shared workflow is a single fix, and an assistant that takes
/// each row as its own investigation pays for the repetition. Confirming a shared cause needs the
/// logs, which nothing here has read, so the grouping is never stated as a finding.
/// </para>
/// </summary>
static class TriagePrompt
{
    /// <summary>
    /// <paramref name="fix"/> is what the caller asked for rather than what is safe here: the
    /// checkouts belong to whoever installed the tool, and editing one they have work in progress
    /// in is their call to make, not this text's.
    /// </summary>
    public static string Build(IReadOnlyList<BuildDto> failing, bool fix, string? filter, int deferred = 0)
    {
        var scope = Scope(filter);
        if (failing.Count == 0)
        {
            return $"Nothing is failing{scope}. There is no triage to do.{DeferredNote(deferred)}";
        }

        var local = failing.Where(_ => _.Directory is { Length: > 0 }).ToList();
        var skipped = failing.Where(_ => _.Directory is not { Length: > 0 }).ToList();
        if (local.Count == 0)
        {
            return NoneLocal(failing, scope) + DeferredNote(deferred);
        }

        var builder = new StringBuilder();
        builder.Append($"{Count(local.Count, "failing build")}{scope} {(local.Count == 1 ? "has its" : "have their")} code checked out locally. Work through {(local.Count == 1 ? "it" : "them")}.");
        builder.Append("\n\n## Failures\n");
        foreach (var group in Grouped(local))
        {
            builder.Append('\n').Append(Heading(group)).Append('\n');
            foreach (var build in group)
            {
                builder.Append(Entry(build));
            }
        }

        builder.Append("\n## Steps\n\n").Append(Procedure(fix));
        if (skipped.Count > 0)
        {
            builder.Append('\n').Append(SkippedNote(skipped));
        }

        builder.Append(DeferredNote(deferred));
        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Says failures were held back on purpose. Without it a red pipeline missing from the list
    /// reads as one that was fixed, or never watched, and the user who deferred it is not
    /// expecting it back in a triage.
    /// </summary>
    static string DeferredNote(int deferred)
    {
        if (deferred == 0)
        {
            return "";
        }

        return $"\n\n{Count(deferred, "failing build")} deferred by the user {(deferred == 1 ? "is" : "are")} left out on purpose. `list_deferred` names {(deferred == 1 ? "it" : "them")}; don't triage {(deferred == 1 ? "it" : "them")} unless asked.";
    }

    /// <summary>
    /// One failure, with its log and its artifacts already on disk. Says where they are rather than
    /// how to fetch them: this is the text the tray puts on the clipboard, so it lands in an
    /// assistant that has no connection to BuildMonitor and no tool to call.
    /// <para>
    /// A method rather than an overload of <see cref="Build"/>. The two resolve by arity, and a
    /// reader at a call site could not tell which of two different documents they were getting:
    /// that one is about ordering and grouping several failures, this one is about a single failure
    /// whose evidence is already gathered.
    /// </para>
    /// </summary>
    public static string One(BuildDto build, TriageFilesDto files, bool fix)
    {
        var builder = new StringBuilder();
        // The opening promise has to match what is actually on disk: a run that produced neither a
        // log nor an artifact still gets a prompt, and one that opened by promising files would
        // have an assistant hunting for a directory that was never written.
        builder.Append(files.Files.Count > 0
            ? "Failing build. Files downloaded. Work through it."
            : "Failing build. Nothing it produced could be downloaded, so work from its code.");
        builder.Append("\n\n## Failure\n");
        builder.Append(Entry(build));
        builder.Append(Evidence(files));
        builder.Append("\n## Steps\n\n");
        builder.Append(Steps(files, fix));
        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Where the files are, or why there are none. The directory is named on its own line and
    /// unquoted: these are absolute paths in the platform's own form, and a Windows one inside a
    /// markdown link loses its separators to escaping.
    /// </summary>
    static string Evidence(TriageFilesDto files)
    {
        var builder = new StringBuilder();
        builder.Append("\n## Files\n\n");
        if (files.Files.Count == 0)
        {
            builder.Append(Nothing(files));
        }
        else
        {
            builder.Append($"Kept {ArtifactStore.Retention.TotalHours:0} hours.");
            if (files.Files.Contains(ArtifactCollector.LogName))
            {
                builder.Append($" `{ArtifactCollector.LogName}` is the whole log.");
            }

            builder.Append("\n\n");
            foreach (var file in files.Files)
            {
                builder.Append($"  {Join(files.Directory, file)}\n");
            }

            builder.Append("\nThis directory is made for triage, outside the repository: never commit it. Unpack inside it, not the checkout.\n");
        }

        builder.Append(Left(files));
        return builder.ToString();
    }

    /// <summary>
    /// Joined on the separator the directory already uses rather than the host's. The store writes
    /// host native paths, so in the running app the two agree; using <see cref="Path.Combine"/>
    /// would additionally make this text depend on which machine rendered it, which for a pure
    /// projection verified as text means a snapshot that only holds on one platform.
    /// </summary>
    static string Join(string directory, string file)
    {
        var separator = directory.LastIndexOfAny(['/', '\\']);
        if (separator < 0)
        {
            return $"{directory}{Path.DirectorySeparatorChar}{file}";
        }

        return $"{directory}{directory[separator]}{file}";
    }

    /// <summary>
    /// Says which kind of nothing it is. A service nobody could ask is not a build that published
    /// none, and an assistant told the second stops looking for evidence that may well exist.
    /// </summary>
    static string Nothing(TriageFilesDto files)
    {
        if (files.Unsupported is { Length: > 0 } unsupported)
        {
            return $"{unsupported}, and the run has no log, so there is nothing on disk for it.\n";
        }

        return "This run published no artifacts and has no log, so there is nothing on disk for it. Work from the build's page and the checkout below.\n";
    }

    /// <summary>
    /// What was left behind, named with its size. Same rule as <see cref="SkippedNote"/>: a list
    /// that quietly drops the one file an assistant is looking for reads as a run that never
    /// published it, and an assistant that believes that stops looking.
    /// </summary>
    static string Left(TriageFilesDto files)
    {
        var builder = new StringBuilder();
        if (files is {Unsupported: { Length: > 0 } unsupported, Files.Count: > 0})
        {
            builder.Append($"\n{unsupported}, so the list above is the log alone rather than everything the run produced.\n");
        }

        if (files.Skipped.Count == 0)
        {
            return builder.ToString();
        }

        builder.Append($"\n{files.Skipped.Count} {(files.Skipped.Count == 1 ? "artifact" : "artifacts")} left out:\n\n");
        foreach (var skipped in files.Skipped)
        {
            builder.Append($"- `{skipped.Name}`: {skipped.Reason}\n");
        }

        builder.Append("\nFetch from the run's page if the log points at one.\n");
        return builder.ToString();
    }

    static string Steps(TriageFilesDto files, bool fix)
    {
        var builder = new StringBuilder();
        var step = 1;
        if (files.Files.Count > 0)
        {
            builder.Append($"{step++}. Read the log, then whatever it points at.\n");
        }

        builder.Append($"{step++}. {Reproduce}\n");
        builder.Append($"{step}. {Fixing(fix, "it", "")}");
        builder.Append("\nIf it isn't code (expired credential, runner or agent problem, outage), say so instead.\n");
        builder.Append(Next());
        return builder.ToString();
    }

    /// <summary>
    /// The answer every user gets until they set a code directory, so it names the option rather
    /// than reporting that there is nothing to do, which would read as a clean build list.
    /// </summary>
    static string NoneLocal(IReadOnlyList<BuildDto> failing, string scope)
    {
        var builder = new StringBuilder();
        builder.Append($"{Count(failing.Count, "failing build")}{scope}, none with a repository checked out locally, so there is no code here to work through.\n\n");
        builder.Append(List(failing));
        builder.Append("\nEither the code directory option is not set, or no checkout under it matched these repositories. Tell the user to set it from the tray's options, then run this again.");
        return builder.ToString();
    }

    /// <summary>
    /// Pipelines holding more than one build first: they are the ones where reading a single log
    /// can close out several rows, and burying them under the singles wastes that.
    /// </summary>
    static IEnumerable<IGrouping<string, BuildDto>> Grouped(List<BuildDto> local) =>
        local
            .GroupBy(_ => _.Pipeline, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(_ => _.Count())
            .ThenBy(_ => _.Key, StringComparer.OrdinalIgnoreCase);

    static string Heading(IGrouping<string, BuildDto> group)
    {
        if (group.Count() == 1)
        {
            return $"### {group.Key}";
        }

        return $"### {group.Key}: {group.Count()} repositories on this one pipeline, so they may share one cause";
    }

    static string Entry(BuildDto build)
    {
        var builder = new StringBuilder();
        builder.Append($"\n- {build.Repo}");
        if (build.Branch is { Length: > 0 } branch)
        {
            builder.Append($" on {branch}");
        }

        builder.Append($", run {build.Run}, failed {build.Timing}\n");
        builder.Append($"  code: {build.Directory}\n");
        builder.Append($"  key: {build.Key}\n");
        if (Commit(build) is { Length: > 0 } commit)
        {
            builder.Append($"  {commit}\n");
        }

        return builder.ToString();
    }

    /// <summary>
    /// The commit line, or empty for a build that has none, as a deployment of an already built
    /// release does.
    /// </summary>
    static string Commit(BuildDto build)
    {
        if (build.Commit is not { Length: > 0 } sha)
        {
            return "";
        }

        var line = $"commit {Short(sha)}";
        if (build.Author is { Length: > 0 } author)
        {
            line += $" by {author}";
        }

        if (Subject(build.CommitMessage) is { Length: > 0 } subject)
        {
            return $"{line}: {subject}";
        }

        return line;
    }

    static string Short(string sha)
    {
        if (sha.Length > 7)
        {
            return sha[..7];
        }

        return sha;
    }

    /// <summary>
    /// The first line of a commit message. Dependabot writes a body longer than everything else on
    /// the row put together, and none of it says anything the subject does not.
    /// </summary>
    static string Subject(string? message)
    {
        if (message is not { Length: > 0 })
        {
            return "";
        }

        var end = message.IndexOf('\n');
        if (end < 0)
        {
            return message.Trim();
        }

        return message[..end].Trim();
    }

    /// <summary>
    /// The checkout under <c>code:</c> is where the user works, not a copy made for this, so an
    /// assistant that finds work in progress in it stops and says so rather than switching the
    /// branch over it: a switch, a stash or a discard can lose what nothing here knows is there.
    /// </summary>
    const string Reproduce = "Reproduce the failure in the `code:` directory. If it has uncommitted work, stop and tell the user to resolve it before continuing. Otherwise check out the build's branch.";

    static string Procedure(bool fix)
    {
        var builder = new StringBuilder();
        builder.Append("1. Read each build's log with `get_build_log`, taking the key from its entry above. Where a log points at a file the run published, such as a test report, a coverage file or a crash dump, call `download_build_artifacts` for that build and read it from the directory that comes back.\n");
        builder.Append("2. Group the failures by what the logs say before investigating any. Repositories failing on one shared workflow, action, dependency or template are one fix, and the pipeline groupings above are only a guess at that.\n");
        builder.Append($"3. {Reproduce} Do this per group, before deciding what it is.\n");
        builder.Append($"4. {Fixing(fix, "each group", " per group")}");
        builder.Append("\nIf a group isn't code (expired credential, runner or agent problem, outage), say so instead.\n");
        builder.Append(Next());
        return builder.ToString();
    }

    /// <summary>
    /// The last thing both prompts say. Triage ends at a decision the user owns, such as which
    /// fix to take, whether to commit it or whether a failure is worth chasing at all, so the
    /// text stops the assistant there and asks rather than carrying on into work nobody asked
    /// for across repositories it was only given to read.
    /// </summary>
    static string Next() =>
        "\nThen stop and ask what to do next, with suggestions. No further changes, commits or repositories without an answer.";

    /// <summary>
    /// The one step that decides whether an assistant edits the user's files, shared by both
    /// prompts so the sentence that authorises a change cannot come to mean two different things
    /// depending on which of them was read.
    /// </summary>
    static string Fixing(bool fix, string subject, string per)
    {
        if (fix)
        {
            return $"Fix {subject} where you reproduced it and run that project's tests. Leave changes uncommitted, say where they are, and don't commit, push or open a PR.\n";
        }

        return $"Report findings{per} and suggested fixes. Change no source files.\n";
    }

    /// <summary>
    /// Named rather than dropped: a list that silently answers for some of the failures reads as
    /// though it answered for all of them.
    /// </summary>
    static string SkippedNote(List<BuildDto> skipped)
    {
        var builder = new StringBuilder();
        builder.Append($"\n{Count(skipped.Count, "other failing build")} {Have(skipped.Count)} no checkout under the code directory and {(skipped.Count == 1 ? "is" : "are")} left out above:\n\n");
        builder.Append(List(skipped));
        return builder.ToString();
    }

    static string List(IReadOnlyList<BuildDto> builds)
    {
        var builder = new StringBuilder();
        foreach (var build in builds)
        {
            builder.Append($"- {Name(build)} on {build.Connection}, failed {build.Timing}\n");
        }

        return builder.ToString();
    }

    /// <summary>
    /// The repository, and the pipeline where it says something more. Octopus and Jenkins name a
    /// project rather than a repository, so the two are often the same text, and printing it twice
    /// reads as two things.
    /// </summary>
    static string Name(BuildDto build)
    {
        if (build.Pipeline.Equals(build.Repo, StringComparison.OrdinalIgnoreCase))
        {
            return build.Repo;
        }

        return $"{build.Repo}, {build.Pipeline}";
    }

    /// <summary>
    /// Whether the caller asked for the failures to be fixed rather than only diagnosed.
    /// <para>
    /// Takes the argument as the string it arrives as. A prompt's arguments are typed as strings by
    /// the protocol, so a bool parameter would refuse a client that sent "true" rather than true,
    /// and the command would error rather than run with its default.
    /// </para>
    /// <para>
    /// Only a word that plainly means yes turns it on, and anything else is off. Claude Code splits
    /// a command's arguments on whitespace and binds them by position, so a repository name typed
    /// one slot too far lands here, and it must not be what starts an assistant editing checkouts.
    /// </para>
    /// </summary>
    public static bool Fixing(string? value) =>
        value?.Trim().ToLowerInvariant() is "true" or "yes" or "on" or "1";

    /// <summary>
    /// The filter to apply, or null for every build. <c>*</c> reads as every build too: arguments
    /// are positional, so without a way to write "everything" in the filter's slot, asking for a fix
    /// across every connection could not be said at all.
    /// </summary>
    public static string? Filter(string? value)
    {
        if (value?.Trim() is { Length: > 0 } text && text != "*")
        {
            return text;
        }

        return null;
    }

    static string Count(int count, string noun)
    {
        if (count == 1)
        {
            return $"One {noun}";
        }

        return $"{count} {noun}s";
    }

    static string Have(int count)
    {
        if (count == 1)
        {
            return "has";
        }

        return "have";
    }

    static string Scope(string? filter)
    {
        if (filter is { Length: > 0 })
        {
            return $" matching \"{filter}\"";
        }

        return "";
    }
}
