public class DurationHistoryTests
{
    [Test]
    public async Task MedianOfRecordedRuns()
    {
        var history = new DurationHistory();
        history.Record("p", TimeSpan.FromMinutes(1));
        history.Record("p", TimeSpan.FromMinutes(9));
        history.Record("p", TimeSpan.FromMinutes(3));
        await Assert.That(history.Median("p")).IsEqualTo(TimeSpan.FromMinutes(3));

        history.Record("p", TimeSpan.FromMinutes(5));
        await Assert.That(history.Median("p")).IsEqualTo(TimeSpan.FromMinutes(4));
    }

    [Test]
    public async Task KeepsOnlyTheLastTen()
    {
        var history = new DurationHistory();
        for (var index = 1; index <= 30; index++)
        {
            history.Record("p", TimeSpan.FromMinutes(index));
        }

        // 21..30 remain, median of which is 25.5
        await Assert.That(history.Median("p")).IsEqualTo(TimeSpan.FromMinutes(25.5));
    }

    [Test]
    public async Task IgnoresNonPositive()
    {
        var history = new DurationHistory();
        history.Record("p", TimeSpan.Zero);
        await Assert.That(history.Median("p")).IsNull();
    }

    [Test]
    public async Task RoundTripsThroughDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"BuildMonitorHistory_{Guid.NewGuid():N}.json");
        try
        {
            var history = new DurationHistory();
            history.Record("a", TimeSpan.FromSeconds(10));
            history.Record("b", TimeSpan.FromSeconds(20));
            history.Save(path);

            var loaded = DurationHistory.Load(path);
            await Verify(loaded.Medians())
                .Snapshot(
                    """
                    {
                      a: 00:00:10,
                      b: 00:00:20
                    }
                    """);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task CorruptFileIsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"BuildMonitorHistory_{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, "not json");
            var loaded = DurationHistory.Load(path);
            await Assert.That(loaded.Medians()).IsEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
