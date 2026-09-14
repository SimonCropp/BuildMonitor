public class RequestBucketTests
{
    // One request a second, up to ten saved.
    static readonly RequestQuota quota = new(60, TimeSpan.FromMinutes(1), 10);

    [Test]
    public async Task RefillsAtItsRateUpToTheBurst()
    {
        var bucket = new RequestBucket(0, Fixtures.Now);
        await Assert.That(bucket.Refill(quota, Fixtures.Now.AddSeconds(4)).Tokens).IsEqualTo(4d);
        await Assert.That(bucket.Refill(quota, Fixtures.Now.AddMinutes(5)).Tokens).IsEqualTo(10d);
    }

    [Test]
    public async Task SpendingPastEmptyIsDebtThatDelaysTheNextToken()
    {
        var bucket = RequestBucket.Full(quota, Fixtures.Now).Spend(12);
        await Assert.That(bucket.Tokens).IsEqualTo(-2d);
        await Assert.That(bucket.NextToken(quota)).IsEqualTo(Fixtures.Now.AddSeconds(3));
    }

    [Test]
    public async Task ABucketWithATokenHasOneNow() =>
        await Assert.That(RequestBucket.Full(quota, Fixtures.Now).NextToken(quota)).IsEqualTo(Fixtures.Now);

    [Test]
    public async Task TimeGoingBackwardsChangesNothing()
    {
        var bucket = new RequestBucket(5, Fixtures.Now);
        await Assert.That(bucket.Refill(quota, Fixtures.Now.AddSeconds(-10))).IsEqualTo(bucket);
    }
}
