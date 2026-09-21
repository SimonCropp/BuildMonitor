public class HistoryDaysOptionTests
{
    [Test]
    public async Task DefaultsToThirty() =>
        await Assert.That(new Settings().HistoryDays).IsEqualTo(30);

    [Test]
    [Arguments("0")]
    [Arguments("366")]
    [Arguments("soon")]
    public async Task OutOfRangeIsRejected(string days)
    {
        var state = MonitorSession.FieldChanged(Fixtures.Options(), FormFields.HistoryDays, days);
        var built = OptionsDraft.TryBuild(Fixtures.OptionsForm(state), state.Settings, out _, out var error);
        await Assert.That(built).IsFalse();
        await Assert.That(error)
            .IsEqualTo(new("The days of builds to show must be between 1 and 365.", FormFields.HistoryDays));
    }

    [Test]
    public async Task SavedWithTheOtherOptions()
    {
        var state = MonitorSession.FieldChanged(Fixtures.Options(), FormFields.HistoryDays, "90");
        var built = OptionsDraft.TryBuild(Fixtures.OptionsForm(state), state.Settings, out var settings, out _);
        await Assert.That(built).IsTrue();
        await Assert.That(settings!.HistoryDays).IsEqualTo(90);
    }
}
