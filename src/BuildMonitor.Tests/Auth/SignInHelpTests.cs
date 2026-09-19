/// <summary>
/// What a failed sign in tells the user. The case worth naming is a personal Microsoft account
/// against Azure DevOps, which Entra reports in full only sometimes and not at all when it turns
/// the address away at its own address box.
/// </summary>
public class SignInHelpTests
{
    static readonly ProviderDescriptor azure = ProviderDescriptors.AzureDevOps;
    static readonly ProviderDescriptor gitHub = ProviderDescriptors.GitHub;

    // Entra's own wording, abridged. The point is that the code is found inside it rather than
    // being the whole message.
    const string tenantMismatch = "AADSTS50020: User account 'jay@bazuzi.com' from identity provider 'live.com' does not exist in tenant 'Contoso' and cannot access the application. Trace ID: 0e6b...";

    [Test]
    public async Task ARefusedAccountSaysToUseAToken()
    {
        var explained = SignInHelp.Explain(azure, AuthResult.Failed(tenantMismatch));
        await Assert.That(explained).IsEqualTo("That is a personal Microsoft account, which cannot sign in to Azure DevOps. Set Sign in with to Token and paste a personal access token.");
    }

    // An address Entra resolved to nothing is as likely a typo in a work address as a personal
    // account, so this one suggests both and concludes neither.
    [Test]
    public async Task AnUnrecognisedAddressDoesNotClaimToKnowWhy()
    {
        var explained = SignInHelp.Explain(azure, AuthResult.Failed("AADSTS50034: The user account does not exist in the directory."));
        await Assert.That(explained).IsEqualTo("Microsoft did not recognise that address. Check it, and if it is a personal Microsoft account it cannot sign in this way: set Sign in with to Token and paste a personal access token.");
    }

    // Nothing came back at all, which is what the user refused at the address box actually sees, so
    // the same advice is offered as a condition rather than as a diagnosis.
    [Test]
    public async Task ATimeoutOffersTheSameAdviceAsAPossibility()
    {
        var explained = SignInHelp.Explain(azure, AuthResult.Expired("The code expired before it was entered."));
        await Assert.That(explained).IsEqualTo("The code expired before it was entered. If the address was refused, it is a personal Microsoft account, which cannot sign in this way. Set Sign in with to Token and paste a personal access token.");
    }

    // A provider that turns no account away has nothing to add, so a timeout stays as it was rather
    // than growing advice that does not apply to it.
    [Test]
    public async Task AProviderWithNoAccountRestrictionIsLeftAlone()
    {
        var expired = AuthResult.Expired("The code expired before it was entered.");
        await Assert.That(SignInHelp.Explain(gitHub, expired)).IsEqualTo("The code expired before it was entered.");
        await Assert.That(SignInHelp.Explain(gitHub, AuthResult.Failed(tenantMismatch))).IsEqualTo(tenantMismatch);
    }

    // Everything else is the provider's to explain: an error BuildMonitor cannot improve on is
    // passed through rather than buried under advice that may have nothing to do with it.
    [Test]
    public async Task AnUnrecognisedFailureIsPassedThrough()
    {
        var explained = SignInHelp.Explain(azure, AuthResult.Failed("AADSTS65001: The user or administrator has not consented."));
        await Assert.That(explained).IsEqualTo("AADSTS65001: The user or administrator has not consented.");
    }

    [Test]
    public async Task AFailureWithNoMessageStillSaysSomething()
    {
        var explained = SignInHelp.Explain(azure, new(false, null, null, null));
        await Assert.That(explained).IsEqualTo("The sign in failed.");
    }
}
