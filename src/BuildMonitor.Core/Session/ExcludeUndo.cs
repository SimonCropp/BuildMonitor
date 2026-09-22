/// <summary>
/// An exclude or a deferral made from a row's menu, kept for as long as the status line reports it
/// so the Undo beside that message can take it back. Either is saved at once and takes its rows
/// with it, and an org or a repo can take dozens, so a slip on the menu otherwise meant the filters
/// page: find the entry, remove it, save.
/// </summary>
/// <param name="Filter">The filter an exclude added, or null for a deferral.</param>
/// <param name="What">What the status line calls it, such as "VerifyTests org", for the message
/// the Undo leaves in its place.</param>
/// <param name="Deferral">The deferral added, or null for an exclude.</param>
record ExcludeUndo(Filter? Filter, string What, Deferral? Deferral = null);
