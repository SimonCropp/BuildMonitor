/// <summary>
/// The native heads drawing each canonical state, captured offscreen. Opt in, because they need
/// a display (xvfb on Linux) and a rasteriser pinned by the CI job; Linux and macOS run the same
/// tests against different renderers, so the baselines are told apart by platform.
/// </summary>
[NotInParallel(nameof(PixelTests))]
public class PixelTests
{
    const int width = 1000;
    const int height = 640;

    static IMonitorWindow? window;

    [Before(Class)]
    public static void Open()
    {
        if (Environment.GetEnvironmentVariable(PixelTestAttribute.Variable) != "true")
        {
            return;
        }

        NativeResolver.Register();
        window = NativeMonitorWindow.Open("BuildMonitor", width, height, null, hidden: true, out var error);
        if (window is null)
        {
            throw new(error!);
        }
    }

    [After(Class)]
    public static void Close()
    {
        window?.Dispose();
        window = null;
    }

    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 1)]
    public Task Builds() =>
        Capture(Fixtures.WithBuilds());

    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 2)]
    public Task Options() =>
        Capture(Fixtures.Options());

    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 3)]
    public Task ConnectionNew() =>
        Capture(Fixtures.ConnectionNew());

    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 4)]
    public Task Filters() =>
        Capture(Fixtures.Filters());

    // The rows of the repositories found under the code directory, whose chip is a folder rather
    // than a word. The only native baseline that shows it drawn.
    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 5)]
    public Task LocalRepos() =>
        Capture(Fixtures.WithLocalRepos());

    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 6)]
    public Task Connections() =>
        Capture(Fixtures.Connections());

    // A pipeline's pull requests on rows of their own, each sorted by its own status, and its main
    // green at the bottom.
    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 7)]
    public Task Lanes() =>
        Capture(Fixtures.WithLanes());

    // A closed group with the host's mark, behind its arrow, among builds that are not indented
    // for it. The other baselines show no group, so nothing else draws how these heads size and
    // place one.
    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 8)]
    public Task Groups() =>
        Capture(Fixtures.WithTwoFailures());

    // A prefix group, open: it names no one repository, so its row has no mark, and its members
    // are indented under it, one naming the repository it came from.
    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 9)]
    public Task PrefixGroup() =>
        Capture(MonitorSession.ToggleGroup(Fixtures.WithPrefixGroup(), Fixtures.VerifyPassing));

    static async Task Capture(SessionState state)
    {
        // Pinned rather than System, so a capture does not depend on the theme of whoever ran it.
        var screen = ScreenBuilder.Build(
            state with
            {
                Settings = state.Settings with
                {
                    Theme = Theme.Dark
                }
            },
            Fixtures.Now);
        using var path = new TempFile("png");
        await Assert.That(window!.Capture(screen, width, height, path)).IsTrue();
        await VerifyFile(path)
            .UniqueForOSPlatform();
    }
}

public sealed class PixelTestAttribute() :
    SkipAttribute($"Set {Variable}=true to run pixel snapshots.")
{
    public const string Variable = "BUILDMONITOR_PIXEL_TESTS";

    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(Environment.GetEnvironmentVariable(Variable) != "true");
}
