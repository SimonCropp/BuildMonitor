/// <summary>
/// A logoff or shutdown, which is how a tray started at login usually ends, never reaches the clean
/// exit that saves the run history, and Windows may end the process once the message that says the
/// session is ending is answered. So the history is saved from inside it.
/// </summary>
public class SessionEndingTests
{
    [Test]
    public async Task TheRunHistoryIsSaved()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"session-ending-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "history.json");
        try
        {
            var history = new DurationHistory();
            history.Record("github/owner/repo/build.yml", TimeSpan.FromMinutes(4));

            MonitorProgram.SessionEnding(history, path)();

            await Assert.That(DurationHistory.Load(path).Median("github/owner/repo/build.yml")).IsEqualTo(TimeSpan.FromMinutes(4));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
