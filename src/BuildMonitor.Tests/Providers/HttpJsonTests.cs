public class HttpJsonTests
{
    static HttpJson AzureDevOps(FakeHttpHandler handler, RateBudget? budget = null) =>
        new(handler, new("https://dev.azure.com/org/"), AuthScheme.BasicEmptyUserToken, "secret", null, budget: budget);

    static HttpJson GitHub(FakeHttpHandler handler, RateBudget? budget = null) =>
        new(handler, new("https://api.github.com/"), AuthScheme.Bearer, "secret", null, budget: budget);

    [Test]
    public async Task ASignInPageAfterARedirectIsAnAuthFailure()
    {
        var handler = new FakeHttpHandler()
            .MapHtml("GET", "https://dev.azure.com/org/_apis/projects", "<html>Sign in</html>", landedOn: "https://spsprodcus4.vssps.visualstudio.com/_signin?realm=dev.azure.com");
        using var client = AzureDevOps(handler);
        await Assert.That(async () => await client.GetText("_apis/projects", Cancel.None)).Throws<AuthException>();
    }

    [Test]
    public async Task ANonAuthoritativeHtmlPageIsAnAuthFailure()
    {
        var handler = new FakeHttpHandler()
            .MapHtml("GET", "https://dev.azure.com/org/_apis/projects", "<html>Sign in</html>", HttpStatusCode.NonAuthoritativeInformation);
        using var client = AzureDevOps(handler);
        await Assert.That(async () => await client.GetText("_apis/projects", Cancel.None)).Throws<AuthException>();
    }

    [Test]
    public async Task HtmlFromTheSameServerIsNotAnAuthFailure()
    {
        var handler = new FakeHttpHandler()
            .MapHtml("GET", "https://dev.azure.com/org/_apis/projects", "<html>Maintenance</html>");
        using var client = AzureDevOps(handler);
        await Assert.That(await client.GetText("_apis/projects", Cancel.None)).IsEqualTo("<html>Maintenance</html>");
    }

    [Test]
    public async Task ASignInPageWhereJsonWasExpectedIsStillAnAuthFailure()
    {
        var handler = new FakeHttpHandler()
            .MapHtml("GET", "https://dev.azure.com/org/_apis/projects", "<html>Sign in</html>", landedOn: "https://spsprodcus4.vssps.visualstudio.com/_signin?realm=dev.azure.com");
        using var client = AzureDevOps(handler);
        await Assert.That(() => client.Get("_apis/projects", AzureDevOpsContext.Default.AzureDevOpsListAzureDevOpsProject, Cancel.None)).Throws<AuthException>();
    }

    [Test]
    public async Task HtmlWhereJsonWasExpectedSaysWhatArrived()
    {
        var handler = new FakeHttpHandler()
            .MapHtml("GET", "https://dev.azure.com/org/_apis/projects", "<html>\n  <title>Maintenance</title>\n</html>");
        using var client = AzureDevOps(handler);
        var exception = await Assert.That(() => client.Get("_apis/projects", AzureDevOpsContext.Default.AzureDevOpsListAzureDevOpsProject, Cancel.None)).Throws<HttpRequestException>();
        await Assert.That(exception!.Message).IsEqualTo("200 OK from https://dev.azure.com/org/_apis/projects: text/html where JSON was expected: <html> <title>Maintenance</title> </html>");
        await Assert.That(exception.StatusCode).IsNull();
    }

    [Test]
    public async Task HtmlWhereJsonWasExpectedIsNotCached()
    {
        var handler = new FakeHttpHandler()
            .MapHtml("GET", "https://dev.azure.com/org/_apis/projects", "<html>Maintenance</html>", HttpStatusCode.OK, null, ("ETag", "\"abc\""));
        using var client = AzureDevOps(handler);
        await Assert.That(() => client.Get("_apis/projects", AzureDevOpsContext.Default.AzureDevOpsListAzureDevOpsProject, Cancel.None)).Throws<HttpRequestException>();
        await Assert.That(client.IsCached("_apis/projects")).IsFalse();
    }

    [Test]
    public async Task HtmlWhereJsonWasExpectedFromASendSaysWhatArrived()
    {
        var handler = new FakeHttpHandler()
            .MapHtml("POST", "https://dev.azure.com/org/_apis/projects", "<html>Maintenance</html>");
        using var client = AzureDevOps(handler);
        var exception = await Assert.That(() => client.Send(HttpMethod.Post, "_apis/projects", null, AzureDevOpsContext.Default.AzureDevOpsListAzureDevOpsProject, Cancel.None)).Throws<HttpRequestException>();
        await Assert.That(exception!.Message).IsEqualTo("200 OK from https://dev.azure.com/org/_apis/projects: text/html where JSON was expected: <html>Maintenance</html>");
    }

    [Test]
    public async Task ALogAcceptsOnlyWhatItAsksFor()
    {
        var handler = new FakeHttpHandler()
            .Get("https://dev.azure.com/org/_apis/build/builds/1/logs/2", "line one\nline two");
        using var client = AzureDevOps(handler);
        await Assert.That(await client.GetLog("_apis/build/builds/1/logs/2", Cancel.None, "text/plain")).IsEqualTo("line one\nline two");
        await Assert.That(handler.RequestHeaders.Single().Accept.ToString()).IsEqualTo("text/plain");
    }

    [Test]
    public async Task ALogIsNotCached()
    {
        var handler = new FakeHttpHandler()
            .Map("GET", "https://dev.azure.com/org/_apis/build/builds/1/logs/2", "log", HttpStatusCode.OK, ("ETag", "\"abc\""));
        using var client = AzureDevOps(handler);
        await client.GetLog("_apis/build/builds/1/logs/2", Cancel.None);
        await Assert.That(client.IsCached("_apis/build/builds/1/logs/2")).IsFalse();
    }

    [Test]
    public async Task AWebPageWhereALogWasExpectedIsRefused()
    {
        var handler = new FakeHttpHandler()
            .MapHtml("GET", "https://dev.azure.com/org/_apis/build/builds/1/logs/2", "<html>Maintenance</html>");
        using var client = AzureDevOps(handler);
        var exception = await Assert.That(async () => await client.GetLog("_apis/build/builds/1/logs/2", Cancel.None)).Throws<HttpRequestException>();
        await Assert.That(exception!.Message).IsEqualTo("200 OK from https://dev.azure.com/org/_apis/build/builds/1/logs/2: text/html where a log was expected: <html>Maintenance</html>");
    }

    [Test]
    public async Task ABitbucketResetIsTheSecondsLeftInTheWindow()
    {
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.bitbucket.org/2.0/repositories/workspace", "{}", HttpStatusCode.TooManyRequests, ("X-RateLimit-Limit", "1000, 1000;w=3600"), ("X-RateLimit-Remaining", "0"), ("X-RateLimit-Reset", "960"));
        using var client = new HttpJson(handler, new("https://api.bitbucket.org/2.0/"), AuthScheme.BasicEmailToken, "secret", "simon@example.com");
        var exception = await Assert.That(async () => await client.GetText("repositories/workspace", Cancel.None)).Throws<RateLimitException>();
        await Assert.That(exception!.RetryAfter).IsEqualTo(TimeSpan.FromSeconds(960));
    }

    [Test]
    public async Task AResetInThePastNamesNoTime()
    {
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/user", "{}", HttpStatusCode.TooManyRequests, ("X-RateLimit-Remaining", "0"), ("X-RateLimit-Reset", "1700000000"));
        using var client = GitHub(handler);
        var exception = await Assert.That(async () => await client.GetText("user", Cancel.None)).Throws<RateLimitException>();
        await Assert.That(exception!.RetryAfter).IsNull();
    }

    [Test]
    public async Task ASecondaryRateLimitIsNotAnAuthFailure()
    {
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/user", """{"message":"You have exceeded a secondary rate limit. Please wait a few minutes before you try again."}""", HttpStatusCode.Forbidden, ("X-RateLimit-Remaining", "4000"));
        using var client = GitHub(handler);
        var exception = await Assert.That(async () => await client.GetText("user", Cancel.None)).Throws<RateLimitException>();
        await Assert.That(exception!.RetryAfter).IsNull();
    }

    [Test]
    public async Task AForbiddenWithRetryAfterIsARateLimit()
    {
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/user", "{}", HttpStatusCode.Forbidden, ("Retry-After", "60"));
        using var client = GitHub(handler);
        var exception = await Assert.That(async () => await client.GetText("user", Cancel.None)).Throws<RateLimitException>();
        await Assert.That(exception!.RetryAfter).IsEqualTo(TimeSpan.FromSeconds(60));
    }

    [Test]
    public async Task TooManyRequestsWithQuotaLeftDoesNotWaitForTheReset()
    {
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/user", "{}", HttpStatusCode.TooManyRequests, ("X-RateLimit-Remaining", "4000"), ("X-RateLimit-Reset", "4102444800"));
        using var client = GitHub(handler);
        var exception = await Assert.That(async () => await client.GetText("user", Cancel.None)).Throws<RateLimitException>();
        await Assert.That(exception!.RetryAfter).IsNull();
    }

    [Test]
    public async Task AForbiddenIsAnAuthExceptionWithItsStatus()
    {
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/orgs/x/repos", """{"message":"Resource protected by organization SAML enforcement."}""", HttpStatusCode.Forbidden);
        using var client = GitHub(handler);
        var exception = await Assert.That(async () => await client.GetText("orgs/x/repos", Cancel.None)).Throws<AuthException>();
        await Assert.That(exception!.Status).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task ANotModifiedStillRecordsTheQuota()
    {
        var budget = new RateBudget(() => Fixtures.Now);
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/user", """{"login":"simon"}""", HttpStatusCode.OK, ("ETag", "\"abc\""), ("X-RateLimit-Limit", "5000"), ("X-RateLimit-Remaining", "4984"), ("X-RateLimit-Reset", "4102444800"));
        using var client = GitHub(handler, budget);
        await client.GetText("user", Cancel.None);
        handler.Map("GET", "https://api.github.com/user", "", HttpStatusCode.NotModified, ("X-RateLimit-Limit", "5000"), ("X-RateLimit-Remaining", "4983"), ("X-RateLimit-Reset", "4102444800"));
        await client.GetText("user", Cancel.None);
        await Assert.That(budget.SentCount).IsEqualTo(2);
        await Assert.That(budget.State.Remaining).IsEqualTo(4983d);
    }

    [Test]
    public async Task ARetryAfterOnASuccessPausesTheBudget()
    {
        var budget = new RateBudget(() => Fixtures.Now);
        var handler = new FakeHttpHandler()
            .Map("GET", "https://dev.azure.com/org/_apis/projects", "{}", HttpStatusCode.OK, ("Retry-After", "30"), ("X-RateLimit-Cost", "0.04905"));
        using var client = AzureDevOps(handler, budget);
        await client.GetText("_apis/projects", Cancel.None);
        await Assert.That(budget.State.PausedUntil).IsEqualTo(Fixtures.Now.AddSeconds(30));
        await Assert.That(budget.CostTotal).IsEqualTo(0.04905);
    }
}
