[TUnit.Core.Executors.STAThreadExecutor]
[NotInParallel(nameof(RowsCanvasTests))]
public class RowsCanvasTests
{
    [Test]
    public async Task ReportsClickedRowsAndDrains()
    {
        using var canvas = new RowsCanvas
        {
            Size = new(1000, 400)
        };
        var screen = ScreenBuilder.Build(Fixtures.WithBuilds(), Fixtures.Now);
        canvas.Apply(screen.Builds!, null);
        using var bitmap = new Bitmap(1000, 400);
        canvas.DrawToBitmap(bitmap, new(0, 0, 1000, 400));

        Click(canvas, MouseButtons.Left, 300, canvas.RowHeight * 2 + 5);
        var input = canvas.Drain();
        await Assert.That(input.ClickedRow).IsEqualTo(2);

        Click(canvas, MouseButtons.Right, 300, canvas.RowHeight + 5);
        input = canvas.Drain();
        await Assert.That(input.RightClickedRow).IsEqualTo(1);
        await Assert.That(input.ClickedRow).IsEqualTo(-1);

        await Assert.That(canvas.Drain().Any).IsFalse();
    }

    [Test]
    public async Task VisibleRowsFollowsTheHeight()
    {
        using var canvas = new RowsCanvas();
        canvas.Size = new(800, canvas.RowHeight * 7 + 3);
        await Assert.That(canvas.VisibleRows).IsEqualTo(7);
    }

    [Test]
    public async Task AWideRowReportsItsChips()
    {
        using var canvas = Drawn(1000);
        var row = FailedRow();
        var input = ClickAlong(canvas, row, _ => _.ClickedChip == ChipKind.CopyLog);
        await Assert.That(input.ClickedChipRow).IsEqualTo(row);
    }

    [Test]
    public async Task ANarrowRowPutsItsLastChipsBehindAnOverflowChip()
    {
        using var canvas = Drawn(640);
        var row = FailedRow();
        var input = ClickAlong(canvas, row, _ => _.ClickedOverflowRow >= 0);
        await Assert.That(input.ClickedOverflowRow).IsEqualTo(row);
        await Assert.That(input.OverflowFrom).IsNotEqualTo(ChipKind.None);
    }

    static RowsCanvas Drawn(int width)
    {
        var canvas = new RowsCanvas
        {
            Size = new(width, 400)
        };
        canvas.Apply(ScreenBuilder.Build(Fixtures.WithBuilds(), Fixtures.Now).Builds!, null);
        using var bitmap = new Bitmap(width, 400);
        canvas.DrawToBitmap(bitmap, new(0, 0, width, 400));
        return canvas;
    }

    static int FailedRow() =>
        Fixtures.RowOf(Fixtures.WithBuilds(), _ => _.Build?.Key == "gh/Verify/test.yml/feature/inline");

    /// <summary>
    /// Clicks along a row from its right end until the canvas reports what is wanted, because where a
    /// chip lands depends on the fonts of the machine running the test.
    /// </summary>
    static MonitorInput ClickAlong(RowsCanvas canvas, int row, Func<MonitorInput, bool> wanted)
    {
        var y = canvas.RowHeight * row + canvas.RowHeight / 2;
        for (var x = canvas.Width - 1; x >= 0; x -= 2)
        {
            Click(canvas, MouseButtons.Left, x, y);
            var input = canvas.Drain();
            if (wanted(input))
            {
                return input;
            }
        }

        throw new("Nothing along the row reported what was wanted");
    }

    static void Click(RowsCanvas canvas, MouseButtons button, int x, int y)
    {
        var method = typeof(Control).GetMethod("OnMouseDown", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(canvas, [new MouseEventArgs(button, 1, x, y, 0)]);
    }
}
