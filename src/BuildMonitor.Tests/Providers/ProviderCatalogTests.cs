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

    [Test]
    public async Task OnlyGitLabSendsASignInsTokenOtherwise()
    {
        var changed = ProviderDescriptors.All
            .Where(descriptor => descriptor.AuthMethods().Any(_ => descriptor.SchemeFor(_) != descriptor.Scheme))
            .Select(_ => $"{_.Id}: {_.SchemeFor(AuthMethod.Device)}");
        await Assert.That(changed).IsEquivalentTo(["gitlab: Bearer"]);
        await Assert.That(ProviderDescriptors.GitLab.SchemeFor(AuthMethod.Browser)).IsEqualTo(AuthScheme.Bearer);
        await Assert.That(ProviderDescriptors.GitLab.SchemeFor(AuthMethod.Token)).IsEqualTo(AuthScheme.HeaderPrivateToken);
    }

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
