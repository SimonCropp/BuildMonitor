/// <summary>
/// One paint of the Windows rows canvas. Measuring every text on each paint cost 26 ms, so with
/// Measure set each paint swaps between two fonts, which empties the widths the canvas keeps, as
/// every paint was before it kept them.
/// </summary>
[MemoryDiagnoser]
public class CanvasBenchmarks
{
    RowsCanvas canvas = new()
    {
        Size = new(1000, 640)
    };

    Bitmap bitmap = new(1000, 640);
    Font[] fonts = [new("Segoe UI", 11f), new("Segoe UI", 11.5f)];
    int paints;

    [Params(false, true)]
    public bool Measure { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        canvas.Font = fonts[0];
        canvas.Apply(ScreenBuilder.Build(LargeAccount.State(), LargeAccount.Now).Builds!, null);
    }

    [Benchmark]
    public void Paint()
    {
        if (Measure)
        {
            canvas.Font = fonts[++paints % 2];
        }

        canvas.DrawToBitmap(bitmap, new(0, 0, bitmap.Width, bitmap.Height));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        canvas.Dispose();
        bitmap.Dispose();
        foreach (var font in fonts)
        {
            font.Dispose();
        }
    }
}
