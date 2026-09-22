public class ConcurrentlyTests
{
    [Test]
    public async Task OneFailingItemDoesNotFailTheOthers()
    {
        var results = await Concurrently.Settle(
            [1, 2, 3],
            2,
            async (item, _) =>
            {
                await Task.Yield();
                if (item == 2)
                {
                    throw new HttpRequestException("boom");
                }

                return item * 10;
            },
            Cancel.None);
        await Assert.That(string.Join(',', results.Select(_ => _.Value))).IsEqualTo("10,0,30");
        await Assert.That(results[1].Exception).IsTypeOf<HttpRequestException>();
    }

    [Test]
    public async Task ProgressNeverStepsBack()
    {
        var reported = new List<int>();
        await Concurrently.Settle(
            Enumerable.Range(0, 500).ToList(),
            16,
            async (item, _) =>
            {
                await Task.Yield();
                return item;
            },
            Cancel.None,
            _ =>
            {
                lock (reported)
                {
                    reported.Add(_.Done);
                }
            });
        await Assert.That(string.Join(',', reported)).IsEqualTo(string.Join(',', Enumerable.Range(0, 501)));
    }

    [Test]
    public async Task CancellationStillEndsEverything()
    {
        using var source = new CancelSource();
        await source.CancelAsync();
        await Assert.That(async () => await Concurrently.Settle([1], 1, (item, _) => Task.FromResult(item), source.Token))
            .Throws<OperationCanceledException>();
    }
}
