/// <summary>
/// Loads the committed native library for this RID and checks it is the one the managed side
/// expects: the version it was built from, the entry points it exports, and the parts of the ABI
/// whose answer is the platform rather than the user. Skipped where none is shipped, which is
/// Windows and any RID without a build.
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

    /// <summary>
    /// Which head has a folder panel of its own, which is what decides whether the app puts one up
    /// or goes looking for zenity. macOS has one, and a head that lost it would send a macOS user
    /// to a chooser that is not installed there and leave Browse doing nothing.
    /// <para>
    /// Asked with nowhere to put a path, which bm.h defines as the question without the request:
    /// asking in earnest would open an NSOpenPanel on a machine nobody is sitting at, and nothing
    /// in a test run would close it.
    /// </para>
    /// </summary>
    [Test]
    [NativeTest]
    public async Task OnlyLinuxSendsTheAppOffToFindAChooser()
    {
        NativeResolver.Register();
        await Assert.That(PanelAnswer()).IsEqualTo(OperatingSystem.IsMacOS() ? 0 : -1);
    }

    /// <summary>
    /// Every entry point the managed side imports is one the shipped library exports. This is the
    /// macOS head's to fail: Swift writes the names out by hand in @_cdecl, where a typo compiles
    /// and is found only by the P/Invoke that misses it, which for bm_pick_directory is a click on
    /// Browse. The C head cannot get them wrong, because the header that declares them is the one
    /// it is compiled against.
    /// <para>
    /// Read off <see cref="Bm"/> rather than listed here, so an entry point added to the ABI is
    /// covered by having been imported rather than by someone remembering this test.
    /// </para>
    /// </summary>
    [Test]
    [NativeTest]
    public async Task EveryEntryPointIsExported()
    {
        NativeResolver.TryFind(out var path);
        var library = NativeLibrary.Load(path!);
        var imported = typeof(Bm)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(_ => _.GetCustomAttribute<LibraryImportAttribute>()?.EntryPoint)
            .OfType<string>()
            .ToList();
        // Named rather than _, which the discard the lookup needs would otherwise bind to.
        var missing = imported
            .Where(entryPoint => !NativeLibrary.TryGetExport(library, entryPoint, out _))
            .ToList();
        await Assert.That(imported).IsNotEmpty();
        await Assert.That(missing).IsEmpty();
    }

    /// <summary>
    /// Through the same P/Invoke the head uses, so a mismatched entry point or signature fails here
    /// rather than on the button. Its own method because a pointer cannot be written in an async
    /// one.
    /// </summary>
    static unsafe int PanelAnswer() =>
        Bm.PickDirectory(null, null, 0);
}

public sealed class NativeTestAttribute() : SkipAttribute("No native renderer is shipped for this RID.")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(!NativeResolver.TryFind(out _));
}
