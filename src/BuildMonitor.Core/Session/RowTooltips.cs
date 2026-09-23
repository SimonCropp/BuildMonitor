/// <summary>
/// What each part of a row says on hover. A row carries five things a click can open, and the cells
/// are narrow enough that most of them are cut short, so this is where a row says what it could not
/// fit and where each link goes. Composed here rather than in a head for the reason every other
/// string on <see cref="BuildRow"/> is: three heads would otherwise name the same link three ways.
/// </summary>
static class RowTooltips
{
    /// <summary>
    /// A build's row. Every entry is optional: a part with nothing of its own to say falls back to
    /// <see cref="RowPart.Row"/> through <see cref="BuildRow.Tooltip"/>, rather than showing a
    /// blank popup.
    /// </summary>
    public static IReadOnlyList<RowTooltip> Of(SessionState state, Build build, string providerName, DateTimeOffset now)
    {
        var run = OpensRun(build);
        var pipeline = $"Open {providerName} history: {build.PipelineName}";
        List<RowTooltip> tooltips =
        [
            new(RowPart.Row, Summary(build, now)),
            new(RowPart.Status, run)
        ];
        // Which cell holds which is decided by the row, so each part says what that row's part
        // opens. A hover that named the usual destination rather than this one would be worse
        // than none: it is the only thing saying where a click goes.
        var repository = build.RepoUrl is { } repo ? $"Open {RepoHosts.NameOf(repo)} project: {build.ShortRepoName()}" : null;
        if (build.LeadsWithRun())
        {
            tooltips.Add(new(RowPart.Name, run));
            tooltips.Add(new(RowPart.Pipeline, pipeline));
            if (repository is not null)
            {
                tooltips.Add(new(RowPart.DetailIcon, repository));
            }
        }
        else
        {
            tooltips.Add(new(RowPart.Pipeline, run));
            tooltips.Add(new(RowPart.DetailIcon, pipeline));
            if (repository is not null)
            {
                tooltips.Add(new(RowPart.Name, repository));
            }
        }

        if (build.BranchUrl is not null &&
            build.ShortBranchName() is { Length: > 0 } branch)
        {
            tooltips.Add(new(RowPart.Branch, $"Open branch: {branch}"));
        }

        if (Timing(state, build) is { Length: > 0 } timing)
        {
            tooltips.Add(new(RowPart.Timing, timing));
        }

        // No entry for the author: the column shortens the name, but the row's own text already
        // gives it in full, so a popup over that cell only ever said what the one beside it said.
        return tooltips;
    }

    /// <summary>
    /// A group's own row. It names the project and a count, and hides everything else while it is
    /// closed, so the hover is where the pipelines it holds are listed.
    /// </summary>
    public static IReadOnlyList<RowTooltip> OfGroup(GroupKey group, ImmutableArray<Build> members, Build latest, DateTimeOffset now)
    {
        var lines = new List<string>
        {
            $"{group.Project}: {Plural(members.Length, "passing build")}"
        };
        lines.AddRange(members
            .Select(_ => _.Branch is null ? _.PipelineName : $"{_.PipelineName} {DetailSpan.BranchIconText}{_.ShortBranchName()}")
            .Distinct(StringComparer.OrdinalIgnoreCase));
        List<RowTooltip> tooltips =
        [
            new(RowPart.Row, string.Join("\n", lines)),
            new(RowPart.Timing, $"Latest: {latest.PipelineName}, {Age(latest, now)}")
        ];
        if (Shared(members) is { } repo)
        {
            tooltips.Add(new(RowPart.Name, $"Open {RepoHosts.NameOf(repo)} project: {group.Project}"));
        }

        return tooltips;
    }

    /// <summary>
    /// The repository every member agrees on, or null. A group is keyed on the repository's short
    /// name, so two members can still be different repositories of the same name on two services,
    /// and a name that opened one of them would be a coin toss.
    /// </summary>
    public static string? Shared(ImmutableArray<Build> members)
    {
        string? shared = null;
        foreach (var member in members)
        {
            if (member.RepoUrl is not { } repo)
            {
                return null;
            }

            if (shared is null)
            {
                shared = repo;
                continue;
            }

            if (!string.Equals(shared, repo, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return shared;
    }

    /// <summary>
    /// What the row cannot say for itself: the whole repository name and branch, which the cells
    /// shorten, the commit and who wrote it. The branch follows the text standing in for its mark,
    /// as it does in the group's hover, since a hover can only be text.
    /// </summary>
    static string Summary(Build build, DateTimeOffset now)
    {
        var lines = new List<string>
        {
            build.Branch is null ? build.RepoName : $"{build.RepoName} {DetailSpan.BranchIconText}{build.Branch}"
        };
        if (build.CommitMessage is not null)
        {
            lines.Add(build.CommitMessage.Split('\n')[0].Trim());
        }

        var details = new List<string>();
        if (build.Author is not null)
        {
            details.Add(build.Author);
        }

        if (build.CommitSha is not null)
        {
            details.Add(build.CommitSha.Length > 7 ? build.CommitSha[..7] : build.CommitSha);
        }

        if (details.Count > 0)
        {
            lines.Add(string.Join(" ", details));
        }

        if (build.Started is { } started)
        {
            lines.Add($"started {Progress.Age(now - started)} ago");
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Every tooltip for something a click opens is written the same way: what it is, then which
    /// one, so the part that varies, and is the part that can run long, comes last.
    /// </summary>
    static string OpensRun(Build build)
    {
        if (build.RunNumberLabel() is { Length: > 0 } number)
        {
            return $"Open run: {number}";
        }

        return "Open run: latest";
    }

    /// <summary>
    /// Where the number beside the bar came from, which the number itself cannot say: a countdown
    /// against the provider's estimate and one against a median of past runs read the same.
    /// </summary>
    static string Timing(SessionState state, Build build)
    {
        if (build.Status == BuildStatus.Running)
        {
            return Estimator.Source(build, state.Medians) switch
            {
                EstimateSource.Provider => "Counting down from the service's estimate",
                EstimateSource.History => "Counting down from the median of recent runs",
                _ => "No estimate yet, so this is how long it has run"
            };
        }

        if (build is {Started: { } started, Finished: { } finished} &&
            finished > started)
        {
            return $"Ran for {Progress.Format(finished - started)}";
        }

        return "";
    }

    static string Age(Build build, DateTimeOffset now)
    {
        var at = build.Finished ?? build.Started ?? build.Queued;
        if (at is null)
        {
            return "never run";
        }

        return $"{Progress.Age(now - at.Value)} ago";
    }

    static string Plural(int count, string noun)
    {
        if (count == 1)
        {
            return $"1 {noun}";
        }

        return $"{count} {noun}s";
    }
}
