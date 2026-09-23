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

    /// <summary>
    /// Pins the two services that have no artifact API, so the flag keeps meaning "this service
    /// cannot" rather than "nobody has implemented it yet". A provider added without artifacts
    /// fails here and has to say which of the two it is.
    /// </summary>
    [Test]
    public async Task OnlyTheServicesWithNoArtifactApiHaveNoArtifacts() =>
        await Assert.That(ProviderDescriptors.All.Where(_ => !_.HasArtifacts).Select(_ => _.Id))
            .IsEquivalentTo(["bitbucket", "travis"]);

    /// <summary>
    /// A provider that says it has artifacts must have overridden both halves: the base class
    /// answers an empty list and throws on a download, which would read as a build that published
    /// nothing rather than as a provider that was never finished.
    /// </summary>
    [Test]
    public async Task EveryProviderWithArtifactsImplementsBothHalves()
    {
        var missing = ProviderDescriptors.All
            .Where(_ => _.HasArtifacts)
            .Select(_ => Providers.Get(_.Id))
            .Where(_ => _.GetType().GetMethod(nameof(IProvider.ListArtifacts))!.DeclaringType == typeof(ProviderBase) ||
                        _.GetType().GetMethod(nameof(IProvider.DownloadArtifact))!.DeclaringType == typeof(ProviderBase))
            .Select(_ => _.Descriptor.Id);
        await Assert.That(missing).IsEmpty();
    }

    /// <summary>
    /// A service said to hold repositories is asked about failed branches, and the base class
    /// throws for one that never learnt to answer; one that answers without saying so is never asked.
    /// </summary>
    [Test]
    public async Task ExactlyTheServicesHoldingRepositoriesAnswerForBranches()
    {
        var answering = Providers.All
            .Where(_ => _.GetType().GetMethod(nameof(IProvider.FateOf))!.DeclaringType != typeof(ProviderBase))
            .Select(_ => _.Descriptor.Id);
        var holding = ProviderDescriptors.All
            .Where(_ => _.HostsRepositories)
            .Select(_ => _.Id);
        await Assert.That(answering).IsEquivalentTo(holding);
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
