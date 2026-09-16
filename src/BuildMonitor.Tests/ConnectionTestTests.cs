public class ConnectionTestTests
{
    [Test]
    public async Task AConnectionThatCanOnlyWatchNamesWhatItNeeds()
    {
        var test = new ConnectionTest(true, "Signed in as simon", BuildAccess.Watch);
        await Assert.That(test.Describe(ProviderDescriptors.AzureDevOps))
            .IsEqualTo("Signed in as simon. The connection can watch builds but not change them. Azure DevOps needs Build (Read & execute)");
    }

    [Test]
    public async Task AConnectionThatCanOnlyWatchWithoutADocumentedPermission()
    {
        var test = new ConnectionTest(true, "Signed in as simon", BuildAccess.Watch);
        await Assert.That(test.Describe(ProviderDescriptors.Jenkins))
            .IsEqualTo("Signed in as simon. The connection can watch builds but not change them");
    }

    [Test]
    public async Task AConnectionThatMayChangeBuildsSaysSo()
    {
        var test = new ConnectionTest(true, "Signed in as simon", BuildAccess.Change);
        await Assert.That(test.Describe(ProviderDescriptors.GitHub))
            .IsEqualTo("Signed in as simon. The connection can watch and change builds");
    }

    [Test]
    public async Task UnknownAddsNothing()
    {
        var test = new ConnectionTest(true, "Signed in as simon");
        await Assert.That(test.Describe(ProviderDescriptors.GitHub)).IsEqualTo("Signed in as simon");
    }

    [Test]
    public async Task AFailedTestIsOnlyItsMessage()
    {
        var test = new ConnectionTest(false, "Bad credentials", BuildAccess.Watch);
        await Assert.That(test.Describe(ProviderDescriptors.GitHub)).IsEqualTo("Bad credentials");
    }
}
