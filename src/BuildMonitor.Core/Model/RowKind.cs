enum RowKind
{
    Build,
    // Two or more passing builds of one project, or two or more running or queued runs of one push,
    // a run set. Carries no build of its own, so nothing that acts on one build can act on it;
    // opens to show its members.
    Group,
    // A build shown under its open group. Its first cell is left empty: the group names the project.
    Member
}
