enum RowKind
{
    Build,
    // Two or more finished builds of one project with one outcome. Carries no build of its own, so
    // nothing that acts on one build can act on it; opens to show its members.
    Group,
    // A build shown under its open group. Its first cell is left empty: the group names the project.
    Member
}
