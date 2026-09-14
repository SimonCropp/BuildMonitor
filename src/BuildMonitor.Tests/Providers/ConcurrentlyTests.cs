public class ConcurrentlyTests
{
    [Test]
    public async Task OneFailingItemDoesNotFailTheOthers()
    {
        var results = await Concurrently.Settle(
            new[] { 1, 2, 3 },
            2,
            async (item, token) =>
            {
                await Task.Yield();
                return item == 2 ? throw new HttpRequestException("boom") : item * 10;
            },
            Cancel.None);
        await Assert.That(string.Join(',', results.Select(_ => _.Value))).IsEqualTo("10,0,30");
        await Assert.That(results[1].Exception).IsTypeOf<HttpRequestException>();
    }

    [Test]
    public async Task CancellationStillEndsEverything()
    {
        using var source = new CancelSource();
        await source.CancelAsync();
        await Assert.That(async () => await Concurrently.Settle(new[] { 1 }, 1, (item, token) => Task.FromResult(item), source.Token))
            .Throws<OperationCanceledException>();
    }
}
