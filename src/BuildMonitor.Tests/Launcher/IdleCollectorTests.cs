public class IdleCollectorTests
{
    [Test]
    public async Task CollectsOnceQuiet()
    {
        using var collector = new IdleCollector(TimeSpan.FromMilliseconds(50));
        var before = GC.CollectionCount(GC.MaxGeneration);
        collector.Touch();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (GC.CollectionCount(GC.MaxGeneration) == before &&
               DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        await Assert.That(GC.CollectionCount(GC.MaxGeneration)).IsGreaterThan(before);
    }
}
