public class RecentPassesTests
{
    [Test]
    public async Task DefaultsToFive() =>
        await Assert.That(new Settings().RecentPasses).IsEqualTo(5);

    [Test]
    [Arguments("-1")]
    [Arguments("51")]
    [Arguments("some")]
    public async Task OutOfRangeIsRejected(string passes)
    {
        var state = MonitorSession.FieldChanged(Fixtures.Options(), FormFields.RecentPasses, passes);
        var built = OptionsDraft.TryBuild(Fixtures.OptionsForm(state), state.Settings, out _, out var error);
        await Assert.That(built).IsFalse();
        await Assert.That(error)
            .IsEqualTo(new("The recent passes to keep out of groups must be between 0 and 50.", FormFields.RecentPasses));
    }

    [Test]
    [Arguments("0", 0)]
    [Arguments("12", 12)]
    public async Task SavedWithTheOtherOptions(string text, int expected)
    {
        var state = MonitorSession.FieldChanged(Fixtures.Options(), FormFields.RecentPasses, text);
        var built = OptionsDraft.TryBuild(Fixtures.OptionsForm(state), state.Settings, out var settings, out _);
        await Assert.That(built).IsTrue();
        await Assert.That(settings!.RecentPasses).IsEqualTo(expected);
    }

    /// <summary>
    /// Verify's two green workflows share a group with none kept out. One kept out leaves the other
    /// a group of one, which is a plain row.
    /// </summary>
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    public Task KeepsTheNewestPassesOutOfGroups(int passes) =>
        Verify(Shape(GreenProject(passes)))
            .UseParameters(passes);

    static SessionState GreenProject(int passes)
    {
        var state = Fixtures.WithGreenProject();
        return state with
        {
            Settings = state.Settings with
            {
                RecentPasses = passes
            }
        };
    }

    static IEnumerable<string> Shape(SessionState state) =>
        RowProjection.Rows(state).Select(_ => $"{_.Kind} {_.Build?.Key} {_.Build?.Status} {_.Group?.Id} {_.Members.Length}");
}
