/// <summary>
/// Which surface the one window is showing. There is exactly one page at a time: the
/// <see cref="Screen"/> describes a single surface, so every head has one window and the text
/// snapshots describe the same thing the pixels do.
/// </summary>
enum Page
{
    Builds,
    Options,
    Filters,
    AddConnection,
    EditConnection,
    SignIn,
    Update,
    RemoveConnection
}
