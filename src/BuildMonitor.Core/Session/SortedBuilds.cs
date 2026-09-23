/// <summary>
/// What <see cref="RowProjection.Pipelines"/> last returned, its builds in that order, and the
/// parts of the state it read, which it hands back while a state holds the same instances of them.
/// See <see cref="RowProjection"/> for why.
/// </summary>
sealed record SortedBuilds(Settings Settings, ImmutableArray<ConnectionState> Connections, ImmutableArray<Build> Builds, ImmutableArray<PipelineBuilds> Pipelines, ImmutableArray<Build> Sorted)
{
    public bool IsFor(SessionState state) =>
        ReferenceEquals(Settings, state.Settings) &&
        Connections == state.Connections &&
        Builds == state.Builds;
}
