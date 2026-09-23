/// <summary>
/// What <see cref="RowProjection.Rows(SessionState, ImmutableArray{PipelineBuilds})"/> last
/// returned and what it was projected from, which it hands back while those are the same
/// instances. See <see cref="RowProjection"/> for why.
/// </summary>
sealed record ProjectedRows(ImmutableArray<PipelineBuilds> Pipelines, ImmutableArray<ConnectionState> Connections, ImmutableHashSet<string> OpenGroups, string Search, ImmutableArray<string> GroupPrefixes, ImmutableArray<Row> Rows)
{
    public bool IsFor(SessionState state, ImmutableArray<PipelineBuilds> pipelines) =>
        Pipelines == pipelines &&
        Connections == state.Connections &&
        ReferenceEquals(OpenGroups, state.Settings.OpenGroups) &&
        Search == state.Search &&
        // Saved prefixes change which builds share a group, and nothing else a projection reads
        // moves when they do: the same builds would have been handed back under the old groups.
        GroupPrefixes == state.Settings.GroupPrefixes;
}
