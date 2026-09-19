/// <summary>
/// Applies the exclusion rules. Pipeline, repo and org rules are checked before a pipeline is
/// fetched, so an excluded pipeline costs no API calls; branch rules can only be checked once the
/// builds are known. A provider that pays a request per repository to discover its pipelines asks
/// <see cref="ExcludesRepo"/> first, so an ignored org costs nothing at all.
/// </summary>
static class Filters
{
    public static bool ExcludesPipeline(IEnumerable<Filter> filters, Pipeline pipeline) =>
        filters.Any(_ => _.Target switch
        {
            FilterTarget.Pipeline => Matches(_, pipeline.Name),
            FilterTarget.Repo => Matches(_, pipeline.RepoName),
            FilterTarget.Org => MatchesOrg(_, pipeline.RepoName),
            _ => false
        });

    /// <summary>
    /// Whether the repository is excluded by what is known of it before its pipelines are: its own
    /// name and the org it sits under. Asked during discovery, where the pipeline names are still
    /// a request away, so a pipeline rule cannot be answered and is left to the poller.
    /// </summary>
    public static bool ExcludesRepo(IEnumerable<Filter> filters, string repoName) =>
        filters.Any(_ => _.Target switch
        {
            FilterTarget.Repo => Matches(_, repoName),
            FilterTarget.Org => MatchesOrg(_, repoName),
            _ => false
        });

    public static bool Excludes(IEnumerable<Filter> filters, Build build) =>
        filters.Any(_ => _.Target switch
        {
            FilterTarget.Pipeline => Matches(_, build.PipelineName),
            FilterTarget.Repo => Matches(_, build.RepoName),
            FilterTarget.Org => MatchesOrg(_, build.RepoName),
            FilterTarget.Branch => build.Branch is not null && Matches(_, build.Branch),
            _ => false
        });

    public static ImmutableArray<Build> Apply(IEnumerable<Filter> filters, IEnumerable<Build> builds)
    {
        var list = filters.ToList();
        if (list.Count == 0)
        {
            return [..builds];
        }

        return [..builds.Where(_ => !Excludes(list, _))];
    }

    static bool MatchesOrg(Filter filter, string repoName) =>
        OrgName.Of(repoName) is { } org &&
        Matches(filter, org);

    public static bool Matches(Filter filter, string value) =>
        filter.Kind switch
        {
            FilterKind.Exact => value.Equals(filter.Text, StringComparison.OrdinalIgnoreCase),
            FilterKind.Prefix => value.StartsWith(filter.Text, StringComparison.OrdinalIgnoreCase),
            FilterKind.Suffix => value.EndsWith(filter.Text, StringComparison.OrdinalIgnoreCase),
            FilterKind.Contains => value.Contains(filter.Text, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
}
