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
    public async Task ADownloadAcceptsOnlyWhatItAsksFor()
    {
        var handler = new FakeHttpHandler()
            .MapBytes("GET", "https://api.github.com/repos/o/r/actions/artifacts/9/zip", [1, 2, 3, 4]);
        using var client = GitHub(handler);
        using var destination = new MemoryStream();
        var written = await client.Download("repos/o/r/actions/artifacts/9/zip", destination, 1024, Cancel.None, "application/zip");
        await Assert.That(written).IsEqualTo(4);
        await Assert.That(destination.ToArray()).IsEquivalentTo(new byte[] {1, 2, 3, 4});
        await Assert.That(handler.RequestHeaders.Single().Accept.ToString()).IsEqualTo("application/zip");
    }

    /// <summary>
    /// The client asks for JSON by default, and a file endpoint that honours it answers with a
    /// description of the file rather than the file, so a download must always replace it.
    /// </summary>
    [Test]
    public async Task ADownloadWithNoAcceptAsksForAnything()
    {
        var handler = new FakeHttpHandler()
            .MapBytes("GET", "https://api.github.com/artifact", [7]);
        using var client = GitHub(handler);
        using var destination = new MemoryStream();
        await client.Download("artifact", destination, 1024, Cancel.None);
        await Assert.That(handler.RequestHeaders.Single().Accept.ToString()).IsEqualTo("*/*");
    }

    [Test]
    public async Task ADownloadIsNotCached()
    {
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/artifact", "zip", HttpStatusCode.OK, ("ETag", "\"abc\""));
        using var client = GitHub(handler);
        using var destination = new MemoryStream();
        await client.Download("artifact", destination, 1024, Cancel.None);
        await Assert.That(client.IsCached("artifact")).IsFalse();
    }

    [Test]
    public async Task ADownloadIsCountedAgainstTheBudget()
    {
        var budget = new RateBudget(() => Fixtures.Now);
        var handler = new FakeHttpHandler()
            .MapBytes("GET", "https://api.github.com/artifact", [1, 2, 3]);
        using var client = GitHub(handler, budget);
        using var destination = new MemoryStream();
        await client.Download("artifact", destination, 1024, Cancel.None);
        await Assert.That(budget.SentCount).IsEqualTo(1);
    }

    [Test]
    public async Task AWebPageWhereAFileWasExpectedIsRefused()
    {
        var handler = new FakeHttpHandler()
            .MapHtml("GET", "https://api.github.com/artifact", "<html>Maintenance</html>");
        using var client = GitHub(handler);
        using var destination = new MemoryStream();
        var exception = await Assert.That(() => client.Download("artifact", destination, 1024, Cancel.None)).Throws<HttpRequestException>();
        await Assert.That(exception!.Message).IsEqualTo("200 OK from https://api.github.com/artifact: text/html where a file was expected: <html>Maintenance</html>");
    }

    [Test]
    public async Task ADeclaredLengthOverTheLimitIsRefusedBeforeReading()
    {
        var handler = new FakeHttpHandler()
            .MapBytes("GET", "https://api.github.com/artifact", [1, 2, 3, 4, 5, 6, 7, 8]);
        using var client = GitHub(handler);
        using var destination = new MemoryStream();
        var exception = await Assert.That(() => client.Download("artifact", destination, 4, Cancel.None)).Throws<ArtifactTooLargeException>();
        await Assert.That(exception!.Declared).IsEqualTo(8);
        await Assert.That(exception.Message).IsEqualTo("8 B, over the 4 B allowed for it");
        // Nothing was copied, so the caller has no partial file to clear up in this case.
        await Assert.That(destination.Length).IsEqualTo(0);
    }

    /// <summary>
    /// The Jenkins case: no Content-Length to weigh up front, so the limit has to be held while
    /// copying.
    /// </summary>
    [Test]
    public async Task ADownloadWithNoDeclaredLengthStopsAtTheLimit()
    {
        var handler = new FakeHttpHandler()
            .MapBytes("GET", "https://api.github.com/artifact", new byte[200000], declareLength: false);
        using var client = GitHub(handler);
        using var destination = new MemoryStream();
        var exception = await Assert.That(() => client.Download("artifact", destination, 1024, Cancel.None)).Throws<ArtifactTooLargeException>();
        await Assert.That(exception!.Declared).IsNull();
        await Assert.That(exception.Message).IsEqualTo("Over the 1 KB allowed for it");
    }

    [Test]
    public async Task ADownloadOfExactlyTheLimitIsKept()
    {
        var handler = new FakeHttpHandler()
            .MapBytes("GET", "https://api.github.com/artifact", [1, 2, 3, 4], declareLength: false);
        using var client = GitHub(handler);
        using var destination = new MemoryStream();
        await Assert.That(await client.Download("artifact", destination, 4, Cancel.None)).IsEqualTo(4);
    }

    [Test]
    public async Task ARefusedDownloadIsAnAuthFailure()
    {
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/artifact", """{"message":"Bad credentials"}""", HttpStatusCode.Unauthorized);
        using var client = GitHub(handler);
        using var destination = new MemoryStream();
        await Assert.That(() => client.Download("artifact", destination, 1024, Cancel.None)).Throws<AuthException>();
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

    [Test]
    public async Task ANotModifiedHandsBackTheValueAlreadyParsed()
    {
        // Kept as bytes, a large account's bodies ran to tens of megabytes, and each 304 parsed its
        // body again.
        var handler = new FakeHttpHandler()
            .Map("GET", "https://dev.azure.com/org/_apis/projects", """{"count":1,"value":[{"id":"p1","name":"Web"}]}""", HttpStatusCode.OK, ("ETag", "\"abc\""));
        using var client = AzureDevOps(handler);
        var first = await client.Get("_apis/projects", AzureDevOpsContext.Default.AzureDevOpsListAzureDevOpsProject, Cancel.None);
        handler.Map("GET", "https://dev.azure.com/org/_apis/projects", "", HttpStatusCode.NotModified);
        var second = await client.Get("_apis/projects", AzureDevOpsContext.Default.AzureDevOpsListAzureDevOpsProject, Cancel.None);

        await Assert.That(ReferenceEquals(first, second)).IsTrue();
        await Assert.That(handler.Requests[^1]).Contains("If-None-Match: \"abc\"");
    }

    [Test]
    public async Task ABodyThatDoesNotParseIsNotCached()
    {
        // Kept, every 304 after it would fail the same way with no request able to fix it.
        var handler = new FakeHttpHandler()
            .Map("GET", "https://dev.azure.com/org/_apis/projects", """{"count":1,"value":"not a list"}""", HttpStatusCode.OK, ("ETag", "\"abc\""));
        using var client = AzureDevOps(handler);
        await Assert.That(() => client.Get("_apis/projects", AzureDevOpsContext.Default.AzureDevOpsListAzureDevOpsProject, Cancel.None)).Throws<HttpRequestException>();
        await Assert.That(client.IsCached("_apis/projects")).IsFalse();
    }

    [Test]
    public async Task AHeaderIsReadWithoutRevalidating()
    {
        // Which headers a 304 repeats is up to the service.
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/user", """{"login":"simon"}""", HttpStatusCode.OK, ("ETag", "\"abc\""), ("X-OAuth-Scopes", "repo, read:org"));
        using var client = GitHub(handler);
        await client.Get("user", GitHubContext.Default.GitHubUser, Cancel.None);
        var scopes = await client.GetHeader("user", "X-OAuth-Scopes", Cancel.None);
        var missing = await client.GetHeader("user", "X-Accepted-OAuth-Scopes", Cancel.None);
        await Assert.That(scopes).IsEqualTo("repo, read:org");
        await Assert.That(missing).IsNull();
        await Assert.That(handler.Requests).IsEquivalentTo(
        [
            "GET https://api.github.com/user",
            "GET https://api.github.com/user",
            "GET https://api.github.com/user"
        ]);
    }

    [Test]
    public async Task AHeaderOfARefusedRequestIsAnAuthFailure()
    {
        var handler = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/user", """{"message":"Bad credentials"}""", HttpStatusCode.Unauthorized);
        using var client = GitHub(handler);
        await Assert.That(() => client.GetHeader("user", "X-OAuth-Scopes", Cancel.None)).Throws<AuthException>();
    }
}
