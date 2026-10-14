/// <summary>
/// A placement crosses the C ABI as <see cref="BmPlacement"/>. When one is passed on is the
/// kernel's <see cref="PlacementSettler"/>, tested there.
/// </summary>
public class NativePlacementTests
{
    [Test]
    public async Task APlacementCrossesTheAbiAndBack()
    {
        var maximized = new WindowPlacement(100, 80, 1000, 640, Maximized: true);
        await Assert.That(NativeMonitorWindow.Placement(NativeMonitorWindow.Placement(maximized))).IsEqualTo(maximized);
        await Assert.That(NativeMonitorWindow.Placement(null).Known).IsEqualTo(0);
    }
}
