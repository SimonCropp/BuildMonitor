public class RateHeadersTests
{
    static readonly DateTimeOffset now = Fixtures.Now;

    static HttpResponseHeaders Headers(params (string Name, string Value)[] values)
    {
        var response = new HttpResponseMessage();
        foreach (var (name, value) in values)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response.Headers;
    }

    [Test]
    public async Task GitHub() =>
        await Assert.That(RateHeaders.Read(Headers(("x-ratelimit-limit", "5000"), ("x-ratelimit-remaining", "4983"), ("x-ratelimit-reset", "1789350300")), now))
            .IsEqualTo(new RateObservation(5000, 4983, DateTimeOffset.FromUnixTimeSeconds(1789350300), null, null, null));

    [Test]
    public async Task GitLab() =>
        await Assert.That(RateHeaders.Read(Headers(("RateLimit-Limit", "2000"), ("RateLimit-Observed", "1"), ("RateLimit-Remaining", "1999"), ("RateLimit-Reset", "1789350300")), now))
            .IsEqualTo(new RateObservation(2000, 1999, DateTimeOffset.FromUnixTimeSeconds(1789350300), null, null, null));

    [Test]
    public async Task BitbucketListsWindowsAndCountsItsResetInSeconds() =>
        await Assert.That(RateHeaders.Read(Headers(("X-RateLimit-Limit", "1000, 1000;w=3600"), ("X-RateLimit-Remaining", "12"), ("X-RateLimit-Reset", "960"), ("X-RateLimit-NearLimit", "true")), now))
            .IsEqualTo(new RateObservation(1000, 12, now.AddSeconds(960), true, null, null));

    [Test]
    public async Task AzureDevOpsCountsInFractionsAndMayPauseOnASuccess() =>
        await Assert.That(RateHeaders.Read(Headers(("X-RateLimit-Cost", "0.04905"), ("X-RateLimit-Remaining", "12.5"), ("Retry-After", "30")), now))
            .IsEqualTo(new RateObservation(null, 12.5, null, null, 0.04905, TimeSpan.FromSeconds(30)));

    [Test]
    public async Task ARetryAfterDateIsRelativeToNow() =>
        await Assert.That(RateHeaders.Read(Headers(("Retry-After", now.AddMinutes(2).ToString("R"))), now).RetryAfter)
            .IsEqualTo(TimeSpan.FromMinutes(2));

    [Test]
    public async Task NoHeadersSayNothing() =>
        await Assert.That(RateHeaders.Read(Headers(), now)).IsEqualTo(RateObservation.None);
}
