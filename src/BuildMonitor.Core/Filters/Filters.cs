/// <summary>
/// Applies the exclusion rules. Pipeline and repo rules are checked before a pipeline is
/// fetched, so an excluded pipeline costs no API calls; branch rules can only be checked once the
/// builds are known.
/// </summary>
static class Filters
{
    public static bool ExcludesPipeline(IEnumerable<Filter> filters, Pipeline pipeline) =>
        filters.Any(_ => _.Target switch
        {
            FilterTarget.Pipeline => Matches(_, pipeline.Name),
            FilterTarget.Repo => Matches(_, pipeline.RepoName),
            _ => false
        });

    public static bool Excludes(IEnumerable<Filter> filters, Build build) =>
        filters.Any(_ => _.Target switch
        {
            FilterTarget.Pipeline => Matches(_, build.PipelineName),
            FilterTarget.Repo => Matches(_, build.RepoName),
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
