public class OAuthFlowsTests
{
    static readonly OAuthClient client = new(
        "test",
        "https://example.com/authorize",
        "https://example.com/token",
        "https://example.com/device",
        "client1",
        null,
        ["read", "write"],
        "http://127.0.0.1:{port}/callback",
        Pkce: true);

    [Test]
    public async Task BrowserFlowRoundTrips()
    {
        var handler = new FakeHttpHandler()
            .Map("POST", "https://example.com/token", """{"access_token":"tok","refresh_token":"ref","expires_in":3600}""");
        string? opened = null;
        var result = await OAuthFlows.Browser(
            client,
            url =>
            {
                opened = url;
                // The user "signs in": the browser follows the redirect straight back.
                var query = LoopbackListener.Parse($"GET {url[url.IndexOf('?')..]} HTTP/1.1");
                var redirect = query["redirect_uri"];
                _ = Task.Run(async () =>
                {
                    using var browser = new HttpClient();
                    await browser.GetAsync($"{redirect}?code=thecode&state={Uri.EscapeDataString(query["state"])}");
                });
            },
            handler,
            0,
            Cancel.None);

        await Assert.That(result.Ok).IsTrue();
        await Assert.That(result.AccessToken).IsEqualTo("tok");
        await Assert.That(result.RefreshToken).IsEqualTo("ref");
        await Assert.That(opened).StartsWith("https://example.com/authorize?client_id=client1&redirect_uri=http%3A%2F%2F127.0.0.1%3A");
        await Assert.That(opened).Contains("code_challenge_method=S256");
        var token = handler.Requests.Single();
        await Assert.That(token).Contains("grant_type=authorization_code");
        await Assert.That(token).Contains("code=thecode");
        await Assert.That(token).Contains("code_verifier=");
        await Assert.That(token).DoesNotContain("client_secret");
    }

    [Test]
    public async Task BrowserFlowRejectsAWrongState()
    {
        var handler = new FakeHttpHandler();
        var result = await OAuthFlows.Browser(
            client,
            url =>
            {
                var query = LoopbackListener.Parse($"GET {url[url.IndexOf('?')..]} HTTP/1.1");
                _ = Task.Run(async () =>
                {
                    using var browser = new HttpClient();
                    await browser.GetAsync($"{query["redirect_uri"]}?code=thecode&state=forged");
                });
            },
            handler,
            0,
            Cancel.None);

        await Assert.That(result.Ok).IsFalse();
        await Assert.That(result.Error).IsEqualTo("The response did not match the request.");
        await Assert.That(handler.Requests).IsEmpty();
    }

    [Test]
    public async Task BrowserFlowReportsADenial()
    {
        var result = await OAuthFlows.Browser(
            client,
            url =>
            {
                var query = LoopbackListener.Parse($"GET {url[url.IndexOf('?')..]} HTTP/1.1");
                _ = Task.Run(async () =>
                {
                    using var browser = new HttpClient();
                    await browser.GetAsync($"{query["redirect_uri"]}?error=access_denied&error_description=The+user+said+no");
                });
            },
            new FakeHttpHandler(),
            0,
            Cancel.None);

        await Assert.That(result.Ok).IsFalse();
        await Assert.That(result.Error).IsEqualTo("The user said no");
    }

    [Test]
    public async Task DeviceFlowPollsUntilGranted()
    {
        var polls = 0;
        var handler = new PollingHandler(() => ++polls switch
        {
            1 => """{"error":"authorization_pending"}""",
            2 => """{"error":"slow_down"}""",
            _ => """{"access_token":"tok"}"""
        });
        var delays = new List<TimeSpan>();
        string? shownCode = null;
        string? shownUrl = null;
        var result = await OAuthFlows.Device(
            client,
            (code, url) =>
            {
                shownCode = code;
                shownUrl = url;
            },
            handler,
            Cancel.None,
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        await Assert.That(result.Ok).IsTrue();
        await Assert.That(result.AccessToken).IsEqualTo("tok");
        await Assert.That(shownCode).IsEqualTo("ABCD-1234");
        await Assert.That(shownUrl).IsEqualTo("https://example.com/activate");
        await Assert.That(delays).IsEquivalentTo([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)]);
    }

    [Test]
    public async Task DeviceFlowStopsOnDenial()
    {
        var handler = new PollingHandler(() => """{"error":"access_denied","error_description":"Denied"}""");
        var result = await OAuthFlows.Device(client, (_, _) => { }, handler, Cancel.None, (_, _) => Task.CompletedTask);
        await Assert.That(result.Ok).IsFalse();
        await Assert.That(result.Error).IsEqualTo("Denied");
        // An answer, so nothing is guessed on top of it.
        await Assert.That(result.TimedOut).IsFalse();
    }

    /// <summary>
    /// What the user refused at the provider's own address box actually gets: the provider never
    /// answers, and the code runs out. Kept apart from a denial so the page can offer the account
    /// advice as a possibility.
    /// </summary>
    [Test]
    public async Task DeviceFlowMarksAnExpiryAsTimedOut()
    {
        var handler = new PollingHandler(() => """{"error":"authorization_pending"}""", expiresIn: 0);
        var result = await OAuthFlows.Device(client, (_, _) => { }, handler, Cancel.None, (_, _) => Task.CompletedTask);
        await Assert.That(result.Ok).IsFalse();
        await Assert.That(result.TimedOut).IsTrue();
        await Assert.That(result.Error).IsEqualTo("The code expired before it was entered.");
    }

    [Test]
    public async Task RefreshSendsTheRefreshGrant()
    {
        var handler = new FakeHttpHandler()
            .Map("POST", "https://example.com/token", """{"access_token":"new","refresh_token":"newer"}""");
        var response = await OAuthFlows.Refresh(client, "old", handler, Cancel.None);
        await Assert.That(response.AccessToken).IsEqualTo("new");
        await Assert.That(handler.Requests.Single()).Contains("grant_type=refresh_token&scope=read+write");
    }

    [Test]
    public async Task TokenRefresherStoresTheNewTokens()
    {
        var secrets = new MemorySecretStore();
        var connection = new Connection
        {
            Id = "gl",
            ProviderId = "gitlab",
            Name = "GitLab",
            ClientId = "app",
            Auth = AuthMethod.Browser
        };
        secrets.Write(SecretKeys.Refresh("gl"), "old");
        var handler = new FakeHttpHandler()
            .Map("POST", "https://gitlab.com/oauth/token", """{"access_token":"new","refresh_token":"newer"}""");
        var refreshed = await new TokenRefresher(secrets, handler).TryRefresh(connection, Cancel.None);
        await Assert.That(refreshed).IsTrue();
        await Assert.That(secrets.Read(SecretKeys.Token("gl"))).IsEqualTo("new");
        await Assert.That(secrets.Read(SecretKeys.Refresh("gl"))).IsEqualTo("newer");
    }

    [Test]
    public async Task TokenRefresherWithoutARefreshTokenDoesNothing()
    {
        var refreshed = await new TokenRefresher(new MemorySecretStore(), new FakeHttpHandler()).TryRefresh(Fixtures.GitHub, Cancel.None);
        await Assert.That(refreshed).IsFalse();
    }

    /// <summary>
    /// The device flow hits the same token endpoint repeatedly with different answers each time.
    /// </summary>
    sealed class PollingHandler(Func<string> next, int expiresIn = 900) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, Cancel cancel)
        {
            var body = request.RequestUri!.AbsolutePath == "/device"
                ? $$"""{"device_code":"dev","user_code":"ABCD-1234","verification_uri":"https://example.com/activate","expires_in":{{expiresIn}},"interval":5}"""
                : next();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
