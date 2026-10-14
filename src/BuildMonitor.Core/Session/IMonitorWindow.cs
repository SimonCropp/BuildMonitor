/// <summary>
/// One platform's renderer, as the kernel's <see cref="IHeadWindow{TScreen, TInput}"/> has it, and
/// what this app asks of one beyond that.
/// </summary>
interface IMonitorWindow : IHeadWindow<Screen, MonitorInput>
{
    /// <summary>
    /// Asks the desktop for a directory, starting at <paramref name="start"/> where it is one.
    /// Null when the user cancelled, or when this desktop has no chooser to ask, which is why the
    /// field beside the button can still be typed into. Here rather than in
    /// <see cref="MonitorActions"/> for the reason the clipboard is: a file dialog belongs to a
    /// toolkit the way a clipboard does.
    /// </summary>
    string? PickDirectory(string? start);
}
