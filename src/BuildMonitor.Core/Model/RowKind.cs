enum RowKind
{
    Build,
    // A connection heading: drawn dimmed, flush left, folds its members.
    Header,
    // Two or more green pipelines of one project in one row. Drawn as a build, so no head needs to
    // know about it; expands back into its pipelines.
    Project
}
