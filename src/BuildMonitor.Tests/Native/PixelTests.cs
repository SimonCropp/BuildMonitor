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
        window = NativeMonitorWindow.Open("BuildMonitor", width, height, hidden: true, out var error);
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

    static async Task Capture(SessionState state)
    {
        var screen = ScreenBuilder.Build(state, Fixtures.Now);
        var path = Path.Combine(Path.GetTempPath(), $"bm-{Guid.NewGuid():N}.png");
        try
        {
            await Assert.That(window!.Capture(screen, width, height, path)).IsTrue();
            await VerifyFile(path)
                .UniqueForOSPlatform();
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public sealed class PixelTestAttribute() : SkipAttribute($"Set {Variable}=true to run pixel snapshots.")
{
    public const string Variable = "BUILDMONITOR_PIXEL_TESTS";

    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(Environment.GetEnvironmentVariable(Variable) != "true");
}
