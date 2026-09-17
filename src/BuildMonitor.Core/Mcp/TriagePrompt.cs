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
    public static string Build(IReadOnlyList<BuildDto> failing, bool fix, string? filter)
    {
        var scope = Scope(filter);
        if (failing.Count == 0)
        {
            return $"Nothing is failing{scope}. There is no triage to do.";
        }

        var local = failing.Where(_ => _.Directory is { Length: > 0 }).ToList();
        var skipped = failing.Where(_ => _.Directory is not { Length: > 0 }).ToList();
        if (local.Count == 0)
        {
            return NoneLocal(failing, scope);
        }

        var builder = new StringBuilder();
        builder.Append($"{Count(local.Count, "failing build")}{scope} {(local.Count == 1 ? "has its" : "have their")} code checked out locally. Work through {(local.Count == 1 ? "it" : "them")}.");
        builder.Append("\n\n## The failures\n");
        foreach (var group in Grouped(local))
        {
            builder.Append('\n').Append(Heading(group)).Append('\n');
            foreach (var build in group)
            {
                builder.Append(Entry(build));
            }
        }

        builder.Append("\n## How to work through them\n\n").Append(Procedure(fix));
        if (skipped.Count > 0)
        {
            builder.Append('\n').Append(SkippedNote(skipped));
        }

        return builder.ToString().TrimEnd();
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
    /// The checkout under <c>code:</c> is where the user works, not a copy made for this, so the
    /// steps keep an assistant from reaching the build's branch by switching it, stashing or
    /// discarding: any of those can lose work in progress that nothing here knows is there. A
    /// worktree gets the same commit and leaves the checkout exactly as it was found.
    /// </summary>
    static string Procedure(bool fix)
    {
        var builder = new StringBuilder();
        builder.Append("1. Read each build's log with `get_build_log`, taking the key from its entry above.\n");
        builder.Append("2. Group the failures by what the logs actually say before investigating any of them. Repositories failing on one shared workflow, action, dependency or template are one fix, not several, and the pipeline groupings above are only a guess at that.\n");
        builder.Append("3. Work each group from the checkout named under `code:`. That checkout is the user's own and may be on another branch or hold uncommitted work, so never switch its branch, stash or discard anything in it. Where the build ran on a branch other than the one checked out, fetch it and add a `git worktree` for it instead. Reproduce the failure before deciding what it is.\n");
        builder.Append(fix
            ? "4. Fix each group where you reproduced it, and run that project's tests. Leave the changes uncommitted, say where they are so the user can review them, and do not commit, push or open a pull request.\n"
            : "4. Report what you found per group, with the fix you would make. Do not change any source files.\n");
        builder.Append("\nWhere a group turns out not to be code at all, such as an expired credential, a runner or agent problem, or a service outage, report it as exactly that rather than looking for a change to make.");
        return builder.ToString();
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
