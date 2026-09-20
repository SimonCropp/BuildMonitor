public class RedirectingHandlerTests
{
    /// <summary>
    /// What the transport could not do: Azure DevOps answers an artifact request from the
    /// organization's address with a redirect to its artifacts host, which wants the personal access
    /// token as well. Dropped there, the download was answered with a sign in page.
    /// </summary>
    [Test]
    public async Task TheCredentialFollowsARedirectInsideAzureDevOps()
    {
        var fake = new FakeHttpHandler()
            .Map("GET", "https://dev.azure.com/org/_apis/build/builds/12/artifacts", "", HttpStatusCode.Found, ("Location", "https://artprodeus21.artifacts.visualstudio.com/org/_apis/artifact/content"))
            .MapBytes("GET", "https://artprodeus21.artifacts.visualstudio.com/org/_apis/artifact/content", [1, 2, 3, 4]);
        using var handler = new RedirectingHandler(fake);
        using var client = AzureDevOps(handler);
        using var destination = new MemoryStream();
        await Assert.That(await client.Download("_apis/build/builds/12/artifacts", destination, 1024, Cancel.None)).IsEqualTo(4);
        await Assert.That(Authorization(fake, 0)).IsNotNull();
        await Assert.That(Authorization(fake, 1)).IsEqualTo(Authorization(fake, 0));
    }

    [Test]
    public async Task TheCredentialFollowsARedirectToAPipelineArtifactBlob()
    {
        var fake = new FakeHttpHandler()
            .Map("GET", "https://dev.azure.com/org/_apis/build/builds/12/artifacts", "", HttpStatusCode.Found, ("Location", "https://vsblobprodcus3.vsblob.vsassets.io/b/content"))
            .MapBytes("GET", "https://vsblobprodcus3.vsblob.vsassets.io/b/content", [1, 2, 3, 4]);
        using var handler = new RedirectingHandler(fake);
        using var client = AzureDevOps(handler);
        using var destination = new MemoryStream();
        await client.Download("_apis/build/builds/12/artifacts", destination, 1024, Cancel.None);
        await Assert.That(Authorization(fake, 1)).IsEqualTo(Authorization(fake, 0));
    }

    /// <summary>
    /// The default the transport set, and what makes GitHub work: the signature is already in the
    /// URL, and the blob store refuses a credential it does not know.
    /// </summary>
    [Test]
    public async Task TheCredentialIsDroppedCrossingToStorage()
    {
        var fake = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/artifact", "", HttpStatusCode.Found, ("Location", "https://productionresultssa0.blob.core.windows.net/artifact?sig=abc"))
            .MapBytes("GET", "https://productionresultssa0.blob.core.windows.net/artifact?sig=abc", [1, 2, 3, 4]);
        using var handler = new RedirectingHandler(fake);
        using var client = new HttpJson(handler, new("https://api.github.com/"), AuthScheme.Bearer, "secret", null);
        using var destination = new MemoryStream();
        await Assert.That(await client.Download("artifact", destination, 1024, Cancel.None)).IsEqualTo(4);
        await Assert.That(Authorization(fake, 0)).IsNotNull();
        await Assert.That(Authorization(fake, 1)).IsNull();
    }

    /// <summary>
    /// A host that merely ends with the same letters is another service, so
    /// <c>notvisualstudio.com</c> is not handed an Azure DevOps token.
    /// </summary>
    [Test]
    public async Task TheCredentialIsDroppedCrossingToALookalikeHost()
    {
        var fake = new FakeHttpHandler()
            .Map("GET", "https://dev.azure.com/org/_apis/build/builds/12/artifacts", "", HttpStatusCode.Found, ("Location", "https://notvisualstudio.com/artifact"))
            .MapBytes("GET", "https://notvisualstudio.com/artifact", [1, 2, 3, 4]);
        using var handler = new RedirectingHandler(fake);
        using var client = AzureDevOps(handler);
        using var destination = new MemoryStream();
        await client.Download("_apis/build/builds/12/artifacts", destination, 1024, Cancel.None);
        await Assert.That(Authorization(fake, 1)).IsNull();
    }

    /// <summary>
    /// Azure DevOps is the only service whose hosts share a credential, so a redirect out of one
    /// service and into it carries nothing either.
    /// </summary>
    [Test]
    public async Task TheCredentialIsDroppedLeavingItsOwnService()
    {
        var fake = new FakeHttpHandler()
            .Map("GET", "https://api.github.com/artifact", "", HttpStatusCode.Found, ("Location", "https://artprodeus21.artifacts.visualstudio.com/org/content"))
            .MapBytes("GET", "https://artprodeus21.artifacts.visualstudio.com/org/content", [1, 2, 3, 4]);
        using var handler = new RedirectingHandler(fake);
        using var client = new HttpJson(handler, new("https://api.github.com/"), AuthScheme.Bearer, "secret", null);
        using var destination = new MemoryStream();
        await client.Download("artifact", destination, 1024, Cancel.None);
        await Assert.That(Authorization(fake, 1)).IsNull();
    }

    /// <summary>
    /// A credential in a header of its own is as secret as one in Authorization, and used to cross
    /// with the request because the transport only knew to drop the one.
    /// </summary>
    [Test]
    public async Task APrivateTokenIsDroppedCrossingToStorage()
    {
        var fake = new FakeHttpHandler()
            .Map("GET", "https://gitlab.com/api/v4/artifact", "", HttpStatusCode.Found, ("Location", "https://storage.googleapis.com/artifact?sig=abc"))
            .MapBytes("GET", "https://storage.googleapis.com/artifact?sig=abc", [1, 2, 3, 4]);
        using var handler = new RedirectingHandler(fake);
        using var client = new HttpJson(handler, new("https://gitlab.com/api/v4/"), AuthScheme.HeaderPrivateToken, "secret", null);
        using var destination = new MemoryStream();
        await client.Download("artifact", destination, 1024, Cancel.None);
        await Assert.That(fake.RequestHeaders[0].Contains("PRIVATE-TOKEN")).IsTrue();
        await Assert.That(fake.RequestHeaders[1].Contains("PRIVATE-TOKEN")).IsFalse();
    }

    [Test]
    public async Task TheCredentialFollowsARedirectOnTheSameHost()
    {
        var fake = new FakeHttpHandler()
            .Map("GET", "https://dev.azure.com/org/_apis/projects", "", HttpStatusCode.Found, ("Location", "/org/_apis/projects?continue=1"))
            .Get("https://dev.azure.com/org/_apis/projects?continue=1", "{}");
        using var handler = new RedirectingHandler(fake);
        using var client = AzureDevOps(handler);
        await Assert.That(await client.GetText("_apis/projects", Cancel.None)).IsEqualTo("{}");
        await Assert.That(fake.Requests[1]).IsEqualTo("GET https://dev.azure.com/org/_apis/projects?continue=1");
        await Assert.That(Authorization(fake, 1)).IsEqualTo(Authorization(fake, 0));
    }

    /// <summary>
    /// What Jenkins answers a stop with. The body is not repeated, which is what every client does
    /// with a 302 and what the service expects.
    /// </summary>
    [Test]
    public async Task ARedirectedPostIsFollowedAsAGet()
    {
        var fake = new FakeHttpHandler()
            .Map("POST", "https://ci.example.com/job/x/1/stop", "", HttpStatusCode.Found, ("Location", "https://ci.example.com/whoAmI/api/json"))
            .Get("https://ci.example.com/whoAmI/api/json", "{}");
        using var handler = new RedirectingHandler(fake);
        using var client = new HttpJson(handler, new("https://ci.example.com/"), AuthScheme.BasicUserToken, "secret", "simon");
        await client.Send(HttpMethod.Post, "job/x/1/stop", new StringContent(""), Cancel.None);
        await Assert.That(fake.Requests[1]).IsEqualTo("GET https://ci.example.com/whoAmI/api/json");
        await Assert.That(Authorization(fake, 1)).IsEqualTo(Authorization(fake, 0));
    }

    /// <summary>
    /// A service that answers every redirect with another one is a loop the poller would sit in
    /// until the call's deadline. The redirect is handed back instead, and reads as the failure it
    /// is.
    /// </summary>
    [Test]
    public async Task ARedirectLoopStops()
    {
        var fake = new FakeHttpHandler()
            .Map("GET", "https://ci.example.com/loop", "", HttpStatusCode.Found, ("Location", "https://ci.example.com/loop"));
        using var handler = new RedirectingHandler(fake);
        using var client = new HttpJson(handler, new("https://ci.example.com/"), AuthScheme.BasicUserToken, "secret", "simon");
        var exception = await Assert.That(async () => await client.GetText("loop", Cancel.None)).Throws<HttpRequestException>();
        await Assert.That(exception!.StatusCode).IsEqualTo(HttpStatusCode.Found);
        await Assert.That(fake.Requests.Count).IsEqualTo(21);
    }

    /// <summary>
    /// Following a Location that is not something to fetch would hand the credential to whatever
    /// answered it, so the redirect is the answer instead.
    /// </summary>
    [Test]
    public async Task ARedirectToAnotherSchemeIsNotFollowed()
    {
        var fake = new FakeHttpHandler()
            .Map("GET", "https://ci.example.com/build", "", HttpStatusCode.Found, ("Location", "mailto:admin@example.com"));
        using var handler = new RedirectingHandler(fake);
        using var client = new HttpJson(handler, new("https://ci.example.com/"), AuthScheme.BasicUserToken, "secret", "simon");
        await Assert.That(async () => await client.GetText("build", Cancel.None)).Throws<HttpRequestException>();
        await Assert.That(fake.Requests.Count).IsEqualTo(1);
    }

    static HttpJson AzureDevOps(HttpMessageHandler handler) =>
        new(handler, new("https://dev.azure.com/org/"), AuthScheme.BasicEmptyUserToken, "secret", null);

    static string? Authorization(FakeHttpHandler handler, int index) =>
        handler.RequestHeaders[index].Authorization?.ToString();
}
