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

    public void SetClipboard(string text) =>
        Calls.Add($"SetClipboard {text}");

    public bool Capture(Screen screen, int width, int height, string pngPath) =>
        false;

    public void Dispose()
    {
    }
}
