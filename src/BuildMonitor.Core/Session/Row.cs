/// <summary>
/// One line of the builds page before it is composed for display: a connection header, a build,
/// or a project's green builds sharing one line. What <see cref="SessionState.SelectedRow"/>
/// indexes.
/// </summary>
/// <param name="Build">Null for a header and for a project, so nothing that acts on one build can
/// act on a row that stands for several.</param>
/// <param name="Members">The builds a project row stands for, most recent first. Empty
/// otherwise.</param>
record Row(RowKind Kind, ConnectionState Connection, Build? Build, bool Folded, ImmutableArray<Build> Members)
{
    /// <summary>
    /// Every build the row stands for, so a count or a listing does not lose the ones a project
    /// row hides.
    /// </summary>
    public ImmutableArray<Build> Builds => Build is null ? Members : [Build];
}
