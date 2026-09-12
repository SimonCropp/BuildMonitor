/// <summary>
/// Loads the committed native library for this RID and checks it is the one the managed side
/// expects. Skipped where none is shipped, which is Windows and any RID without a build.
/// </summary>
public class NativeTests
{
    [Test]
    [NativeTest]
    public async Task VersionMatches()
    {
        NativeResolver.Register();
        await Assert.That(Bm.Version()).IsEqualTo(Bm.ExpectedVersion);
    }

    [Test]
    [NativeTest]
    public async Task TrayAvailabilityMatchesThePlatform()
    {
        NativeResolver.Register();
        await Assert.That(Bm.TrayAvailable()).IsEqualTo(OperatingSystem.IsMacOS() ? 1 : 0);
    }
}

public sealed class NativeTestAttribute() : SkipAttribute("No native renderer is shipped for this RID.")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(!NativeResolver.TryFind(out _));
}
