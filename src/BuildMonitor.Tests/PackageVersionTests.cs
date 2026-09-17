public class PackageVersionTests
{
    [Test]
    [Arguments("0.1.0-beta.9", "0.1.0-beta.10")]
    [Arguments("0.1.0-beta.10", "0.1.0")]
    [Arguments("0.1.0", "0.2.0")]
    [Arguments("0.9.0", "0.10.0")]
    [Arguments("1.0.0", "1.0.0.1")]
    [Arguments("1.0.0-alpha", "1.0.0-alpha.1")]
    [Arguments("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [Arguments("1.0.0-alpha.beta", "1.0.0-beta")]
    [Arguments("1.0.0-beta.11", "1.0.0-rc.1")]
    public async Task OrdersAsNuGetDoes(string lower, string higher)
    {
        await Assert.That(PackageVersion.Compare(lower, higher)).IsLessThan(0);
        await Assert.That(PackageVersion.Compare(higher, lower)).IsGreaterThan(0);
    }

    [Test]
    [Arguments("0.1.0-beta.9", "0.1.0-beta.9")]
    [Arguments("1.0", "1.0.0")]
    [Arguments("1.0.0-Beta", "1.0.0-beta")]
    [Arguments("1.0.0+abc", "1.0.0")]
    public async Task Equal(string left, string right)
    {
        await Assert.That(PackageVersion.Compare(left, right)).IsEqualTo(0);
        await Assert.That(PackageVersion.Compare(right, left)).IsEqualTo(0);
    }
}
