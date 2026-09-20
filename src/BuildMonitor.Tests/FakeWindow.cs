/// <summary>
/// An <see cref="IMonitorWindow"/> that records what the applier asked of it.
/// </summary>
class FakeWindow : IMonitorWindow
{
    public List<string> Calls { get; } = [];
    public Screen? Last { get; private set; }
    public MonitorInput Next { get; set; }

    public bool Present(Screen screen)
    {
        Last = screen;
        return true;
    }

    public MonitorInput Poll()
    {
        var input = Next;
        Next = new();
        return input;
    }

    public void SetHidden(bool hidden) =>
        Calls.Add($"SetHidden {hidden}");

    public void Focus() =>
        Calls.Add("Focus");

    /// <summary>
    /// Whether the desktop refuses the text, which is what a Windows clipboard another process has
    /// open does. False by default, so a test that never sets it copies.
    /// </summary>
    public bool ClipboardBusy { get; set; }

    public bool SetClipboard(string text)
    {
        Calls.Add($"SetClipboard {text}");
        return !ClipboardBusy;
    }

    /// <summary>
    /// What the chooser returns next, or null for a cancel, which is the default so a test that
    /// never sets one cannot accidentally pick a directory.
    /// </summary>
    public string? Picked { get; set; }

    public string? PickDirectory(string? start)
    {
        Calls.Add($"PickDirectory {start}");
        return Picked;
    }

    public bool Capture(Screen screen, int width, int height, string pngPath) =>
        false;

    public void Dispose()
    {
    }
}
