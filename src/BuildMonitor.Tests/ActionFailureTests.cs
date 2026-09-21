public class ActionFailureTests
{
    [Test]
    public async Task RefusedNamesThePermission()
    {
        var message = ActionFailure.Describe(ProviderDescriptors.AzureDevOps, "Retrying TheProject e2e", new AuthException("401 Unauthorized"));
        await Assert.That(message).IsEqualTo("Retrying TheProject e2e failed: the connection can watch builds but not change them. Azure DevOps needs Build (Read & execute)");
    }

    [Test]
    public async Task RefusedWithoutADocumentedPermission()
    {
        var message = ActionFailure.Describe(ProviderDescriptors.Jenkins, "Cancelling deploy", new AuthException("403 Forbidden", HttpStatusCode.Forbidden));
        await Assert.That(message).IsEqualTo("Cancelling deploy failed: 403 Forbidden. The connection may not be allowed to change builds");
    }

    [Test]
    public async Task OtherFailuresKeepTheirMessage()
    {
        var message = ActionFailure.Describe(ProviderDescriptors.AzureDevOps, "Retrying TheProject e2e", new HttpRequestException("500 Internal Server Error"));
        await Assert.That(message).IsEqualTo("Retrying TheProject e2e failed: 500 Internal Server Error");
    }

    [Test]
    public async Task UnknownConnectionKeepsTheMessage()
    {
        var message = ActionFailure.Describe(Fixtures.WithBuilds(), "missing", "Retrying TheProject e2e", new AuthException("401 Unauthorized"));
        await Assert.That(message).IsEqualTo("Retrying TheProject e2e failed: 401 Unauthorized");
    }
}
