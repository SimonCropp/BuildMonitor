/// <summary>
/// One line of the builds page before it is composed for display: either a connection header or
/// a build. What <see cref="SessionState.SelectedRow"/> indexes.
/// </summary>
record Row(RowKind Kind, ConnectionState Connection, Build? Build, bool Folded);
