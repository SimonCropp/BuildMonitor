/// <summary>
/// One line of the builds page before it is composed for display: a build, a group's header, or a
/// build under its open group. What <see cref="SessionState.SelectedRow"/> indexes.
/// </summary>
/// <param name="Connection">The build's connection. Null for a group, whose members can come from
/// several.</param>
/// <param name="Build">Null for a group, so nothing that acts on one build can act on a row that
/// stands for several.</param>
/// <param name="Group">The group a header or member row belongs to. Null for a build on its own.</param>
/// <param name="Expanded">Whether a group's members follow its row.</param>
/// <param name="Members">The builds a group stands for, most recent first. Empty otherwise.</param>
record Row(RowKind Kind, ConnectionState? Connection, Build? Build, GroupKey? Group, bool Expanded, ImmutableArray<Build> Members)
{
    /// <summary>
    /// Every build the row stands for, so a search does not lose the ones a closed group hides.
    /// </summary>
    public ImmutableArray<Build> Builds => Build is null ? Members : [Build];
}
