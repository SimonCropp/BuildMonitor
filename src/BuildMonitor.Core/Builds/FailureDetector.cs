/// <summary>
/// Which builds in a poll are failures the user has not been told about. A failure that was
/// already failed last poll is not news, and neither is anything on a connection's first poll:
/// the app starting is not the moment to announce every red pipeline.
/// </summary>
static class FailureDetector
{
    public static ImmutableArray<Build> NewFailures(ImmutableArray<Build> previous, ImmutableArray<Build> next)
    {
        if (previous.Length == 0)
        {
            return [];
        }

        var known = previous
            .Where(_ => _.Status == BuildStatus.Failed)
            .Select(_ => (_.Key, _.RunNumber))
            .ToHashSet();
        return
        [
            ..next.Where(_ => _.Status == BuildStatus.Failed && !known.Contains((_.Key, _.RunNumber)))
        ];
    }

    public static Notification? Describe(ImmutableArray<Build> failures)
    {
        if (failures.Length == 0)
        {
            return null;
        }

        if (failures.Length == 1)
        {
            var build = failures[0];
            var branch = build.Branch is null ? "" : $" {build.Branch}";
            return new($"{build.PipelineName} failed", $"{build.RepoName}{branch} {build.RunNumberLabel()}".Trim());
        }

        return new(
            $"{failures.Length} builds failed",
            string.Join(", ", failures.Select(_ => _.PipelineName).Distinct()));
    }
}
