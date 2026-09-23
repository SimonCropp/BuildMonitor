enum RowKind
{
    Build,
    // Two or more finished builds of one project with one outcome. Carries no build of its own, so
    // nothing that acts on one build can act on it; opens to show its members.
    Group,
    // A build shown under its open group. Its first cell is left empty: the group names the project.
    Member,
    // A run on another branch of the pipeline whose row it follows, there while it is running,
    // queued or failed. Composed as a build's row with its first cell and its marks left empty, the
    // row above having named them, so a head is handed it as a Build and never sees this.
    Lane
}
