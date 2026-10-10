/// <summary>
/// Which runtimes/{rid} directories the resolver probes, which is what decides whose build of the
/// native library a process loads. Nothing here loads one, so these run on every platform.
/// </summary>
public class NativeResolverTests
{
    /// <summary>
    /// The probe for a musl RID is that RID alone. The linux-{arch} candidate after it names the
    /// glibc build, which a musl process must not load.
    /// </summary>
    [Test]
    [Arguments("linux-musl-x64")]
    [Arguments("linux-musl-arm64")]
    public async Task MuslProbesItsOwnRidAndNothingElse(string runtimeIdentifier)
    {
        var rids = NativeResolver.Rids(runtimeIdentifier).ToList();
        await Assert.That(rids).IsEquivalentTo([runtimeIdentifier]);
    }

    /// <summary>
    /// Everywhere else the synthesised RID still follows the framework's own.
    /// </summary>
    [Test]
    public async Task OtherRidsStillFallBackToTheSynthesisedRid()
    {
        var rids = NativeResolver.Rids("some-rid").ToList();
        await Assert.That(rids.Count).IsGreaterThan(1);
        await Assert.That(rids[0]).IsEqualTo("some-rid");
    }
}
