public class ETagCacheTests
{
    [Test]
    public async Task AnEntryRequestedEachCycleIsKept()
    {
        var cache = new ETagCache();
        cache.Set("url", "\"a\"", [1]);
        cache.Rotate();
        await Assert.That(cache.TryGet("url", out _)).IsTrue();
        cache.Rotate();
        await Assert.That(cache.TryGet("url", out var entry)).IsTrue();
        await Assert.That(entry.ETag).IsEqualTo("\"a\"");
    }

    [Test]
    public async Task ContainsDoesNotKeepAnEntryAlive()
    {
        var cache = new ETagCache();
        cache.Set("url", "\"a\"", [1]);
        cache.Rotate();
        await Assert.That(cache.Contains("url")).IsTrue();
        cache.Rotate();
        await Assert.That(cache.Contains("url")).IsFalse();
    }

    [Test]
    public async Task AnEntryNotRequestedForAWholeCycleIsDropped()
    {
        var cache = new ETagCache();
        cache.Set("url", "\"a\"", [1]);
        cache.Rotate();
        cache.Rotate();
        await Assert.That(cache.TryGet("url", out _)).IsFalse();
    }
}
