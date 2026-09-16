public class ProviderCatalogTests
{
    [Test]
    public async Task EveryDescriptorHasAProvider()
    {
        var descriptors = ProviderDescriptors.All.Select(_ => _.Id).ToList();
        var providers = Providers.All.Select(_ => _.Descriptor.Id).ToList();
        await Assert.That(providers).IsEquivalentTo(descriptors);
    }

    [Test]
    public Task Descriptors() =>
        Verify(ProviderDescriptors.All);

    /// <summary>
    /// A sign in gets an OAuth token, which each service with a sign in shows sent as a Bearer
    /// token, whatever a pasted token goes as. GitLab refuses one in PRIVATE-TOKEN.
    /// </summary>
    [Test]
    public async Task ASignInsTokenIsABearerToken()
    {
        var signIns = ProviderDescriptors.All
            .SelectMany(descriptor => descriptor.AuthMethods()
                .Where(_ => _ != AuthMethod.Token)
                .Select(_ => $"{descriptor.Id} {_}: {descriptor.SchemeFor(_)}"));
        await Assert.That(signIns).IsEquivalentTo(
        [
            "azure-devops Browser: Bearer",
            "azure-devops Device: Bearer",
            "github Browser: Bearer",
            "github Device: Bearer",
            "gitlab Browser: Bearer",
            "gitlab Device: Bearer"
        ]);
    }

    [Test]
    public async Task APastedTokenGoesAsItsProvidersScheme() =>
        await Assert.That(ProviderDescriptors.All.Where(_ => _.SchemeFor(AuthMethod.Token) != _.Scheme)).IsEmpty();

    [Test]
    [Arguments(AuthScheme.Bearer, "Bearer secret")]
    [Arguments(AuthScheme.Token, "token secret")]
    [Arguments(AuthScheme.BasicUserToken, "Basic c2ltb246c2VjcmV0")]
    [Arguments(AuthScheme.BasicEmptyUserToken, "Basic OnNlY3JldA==")]
    [Arguments(AuthScheme.BasicEmailToken, "Basic c2ltb246c2VjcmV0")]
    public async Task AuthorizationHeader(AuthScheme scheme, string expected)
    {
        using var client = new HttpClient();
        Credential.Apply(client.DefaultRequestHeaders, scheme, "secret", "simon");
        await Assert.That(client.DefaultRequestHeaders.Authorization!.ToString()).IsEqualTo(expected);
    }
}
