/// <summary>
/// An exclude made from a row's menu, kept for as long as the status line reports it so the Undo
/// beside that message can take it back. An exclude is saved at once and takes its rows with it,
/// and an org or a repo can take dozens, so a slip on the menu otherwise meant the filters page:
/// find the filter, remove it, save.
/// </summary>
/// <param name="What">What the status line calls it, such as "VerifyTests org", for the message
/// the Undo leaves in its place.</param>
record ExcludeUndo(Filter Filter, string What);
