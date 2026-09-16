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

    /// <summary>
    /// The folder chip is a picture rather than a label, so it is the one whose width does not come
    /// from measuring text. This says it still lands somewhere a click can reach.
    /// </summary>
    [Test]
    public async Task ARowReportsItsFolderChip()
    {
        var state = Fixtures.WithLocalRepos();
        using var canvas = Drawn(1000, state);
        var row = Fixtures.RowOf(state, _ => _.Build?.Key == "gh/DiffEngine/test.yml/main");
        var input = ClickAlong(canvas, row, _ => _.ClickedChip == ChipKind.OpenDirectory);
        await Assert.That(input.ClickedChipRow).IsEqualTo(row);
    }

    [Test]
    [Arguments(nameof(ChipKind.Build))]
    [Arguments(nameof(ChipKind.Branch))]
    public async Task ARowReportsTheLinksInItsText(string link)
    {
        using var canvas = Drawn(1000);
        var row = FailedRow();
        var input = ClickAlong(canvas, row, _ => _.ClickedChip == Enum.Parse<ChipKind>(link));
        await Assert.That(input.ClickedChipRow).IsEqualTo(row);
    }

    [Test]
    public async Task ANameThatIsTheBuildsReportsTheRun()
    {
        // Octopus names the pipeline after the project, so the name is the only place the run is named.
        using var canvas = Drawn(1000);
        var row = Fixtures.RowOf(Fixtures.WithBuilds(), _ => _.Build?.PipelineName == "Deploy Web");
        var input = ClickAlong(canvas, row, _ => _.ClickedChip == ChipKind.Build);
        await Assert.That(input.ClickedChipRow).IsEqualTo(row);
    }

    [Test]
    public async Task ANarrowRowPutsItsLastChipsBehindAnOverflowChip()
    {
        // Narrow enough that the bar has already given way and the chips still do not all fit.
        using var canvas = Drawn(520);
        var row = FailedRow();
        var input = ClickAlong(canvas, row, _ => _.ClickedOverflowRow >= 0);
        await Assert.That(input.ClickedOverflowRow).IsEqualTo(row);
        await Assert.That(input.OverflowFrom).IsNotEqualTo(ChipKind.None);
    }

    [Test]
    public async Task AFontChangeMeasuresTheTextAgain()
    {
        // Widths are kept between paints, so a canvas that kept those of the font it was first drawn
        // in would size its columns for text smaller than it draws.
        using var larger = new Font("Segoe UI", 16f);
        using var changed = Drawn(1000);
        changed.Font = larger;
        using var fresh = new RowsCanvas
        {
            Font = larger,
            Size = new(1000, 400)
        };
        fresh.Apply(ScreenBuilder.Build(Fixtures.WithBuilds(), Fixtures.Now).Builds!, null);

        await Assert.That(Png(changed).SequenceEqual(Png(fresh))).IsTrue();
    }

    static byte[] Png(RowsCanvas canvas)
    {
        using var bitmap = new Bitmap(canvas.Width, canvas.Height);
        canvas.DrawToBitmap(bitmap, new(0, 0, canvas.Width, canvas.Height));
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }

    static RowsCanvas Drawn(int width) =>
        Drawn(width, Fixtures.WithBuilds());

    static RowsCanvas Drawn(int width, SessionState state)
    {
        var canvas = new RowsCanvas
        {
            Size = new(width, 400)
        };
        canvas.Apply(ScreenBuilder.Build(state, Fixtures.Now).Builds!, null);
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
