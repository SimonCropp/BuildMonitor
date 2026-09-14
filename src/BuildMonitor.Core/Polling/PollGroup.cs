/// <summary>
/// The pipelines one fetch covers. Groups keep the order pipelines were discovered in: providers
/// list the most recently pushed or updated first, which is the order a limited quota should be
/// spent in after a start.
/// </summary>
record PollGroup(string Key, ImmutableArray<Pipeline> Pipelines)
{
    public static string KeyOf(FetchUnit unit, Pipeline pipeline) =>
        unit switch
        {
            FetchUnit.Repository => pipeline.RepoName,
            FetchUnit.Connection => "",
            _ => pipeline.Id
        };

    public static ImmutableArray<PollGroup> Of(FetchUnit unit, IEnumerable<Pipeline> pipelines) =>
    [
        ..pipelines
            .GroupBy(_ => KeyOf(unit, _))
            .Select(_ => new PollGroup(_.Key, [.._]))
    ];
}
