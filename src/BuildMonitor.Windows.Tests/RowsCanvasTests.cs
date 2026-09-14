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

    static void Click(RowsCanvas canvas, MouseButtons button, int x, int y)
    {
        var method = typeof(Control).GetMethod("OnMouseDown", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(canvas, [new MouseEventArgs(button, 1, x, y, 0)]);
    }
}
