/// <summary>
/// What a second costs the Windows rows canvas: the page as the clock rebuilds it a second on, and
/// the rows drawn for it, to a buffer like the one the control paints to. Every row, as each second
/// used to draw, against the rows the second changed. At the top of the large account nearly every
/// row in view is running, and says something new each second; scrolled past those, the rows have
/// settled and say the same for a minute at a time.
/// </summary>
[MemoryDiagnoser]
public class CanvasTickBenchmarks
{
    static Rectangle everything = new(0, 0, 1000, 640);

    RowsCanvas canvas = new()
    {
        Size = everything.Size
    };

    Font font = new("Segoe UI", 11f);
    Graphics screen = null!;
    BufferedGraphics buffer = null!;
    BuildsPage[] pages = [];
    int turn;

    [Params(false, true)]
    public bool Settled { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var state = LargeAccount.State();
        if (Settled)
        {
            state = MonitorSession.ScrollTo(state, 100);
        }

        pages =
        [
            ScreenBuilder.Build(state, LargeAccount.Now).Builds!,
            ScreenBuilder.Build(state, LargeAccount.Now + TimeSpan.FromSeconds(1)).Builds!
        ];
        screen = Graphics.FromHwnd(IntPtr.Zero);
        buffer = BufferedGraphicsManager.Current.Allocate(screen, everything);
        canvas.Font = font;
        canvas.Apply(pages[0], null);
        canvas.PaintRows(buffer.Graphics, everything);
    }

    [Benchmark(Baseline = true)]
    public void EveryRow()
    {
        turn = 1 - turn;
        canvas.Apply(pages[turn], null);
        buffer.Graphics.ResetClip();
        canvas.PaintRows(buffer.Graphics, everything);
    }

    [Benchmark]
    public void ChangedRows()
    {
        var previous = pages[turn];
        turn = 1 - turn;
        canvas.Apply(pages[turn], null);
        if (RowsCanvas.Changed(previous, pages[turn]) is not { } rows)
        {
            buffer.Graphics.ResetClip();
            canvas.PaintRows(buffer.Graphics, everything);
            return;
        }

        foreach (var row in rows)
        {
            var clip = new Rectangle(0, row * canvas.RowHeight, everything.Width, canvas.RowHeight);
            buffer.Graphics.SetClip(clip);
            canvas.PaintRows(buffer.Graphics, clip);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        canvas.Dispose();
        buffer.Dispose();
        screen.Dispose();
        font.Dispose();
    }
}
