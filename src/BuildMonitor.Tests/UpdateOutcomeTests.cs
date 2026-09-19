/// <summary>
/// The report an update leaves for the tray it starts again.
/// </summary>
public class UpdateOutcomeTests
{
    /// <summary>
    /// What dotnet tool update wrote when a running MCP server held the old version's files.
    /// </summary>
    const string locked =
        """
        failed
        Tool 'buildmonitor' failed to update due to the following:
        Failed to uninstall tool package 'buildmonitor': Access to the path 'C:\Users\me\.dotnet\tools\.store\buildmonitor\0.1.0-beta.6' is denied.
        """;

    [Test]
    public async Task TheFailureIsReportedOnceAndThenRemoved()
    {
        using var path = new TempFile();
        // As Windows PowerShell's Set-Content -Encoding UTF8 writes it, with a byte order mark.
        await File.WriteAllTextAsync(path, locked, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var notification = UpdateOutcome.Take(path);

        await Assert.That(notification).IsEqualTo(new("Update failed", """Failed to uninstall tool package 'buildmonitor': Access to the path 'C:\Users\me\.dotnet\tools\.store\buildmonitor\0.1.0-beta.6' is denied."""));
        await Assert.That(File.Exists(path)).IsFalse();
        await Assert.That(UpdateOutcome.Take(path)).IsNull();
    }

    /// <summary>
    /// The update that worked is reported too, not only the one that did not: without it the tray
    /// coming back is the only sign anything happened, and that looks the same either way.
    /// </summary>
    [Test]
    public async Task TheSuccessIsReported()
    {
        using var path = new TempFile();
        await File.WriteAllTextAsync(path, "ok\nTool 'buildmonitor' was successfully updated from version '0.1.0-beta.10' to version '0.1.0-beta.11'.\n");

        var notification = UpdateOutcome.Take(path);

        await Assert.That(notification?.Title).IsEqualTo("BuildMonitor updated");
        await Assert.That(File.Exists(path)).IsFalse();
    }

    /// <summary>
    /// What an update started by a version that wrote no status line leaves behind. Read as the
    /// failure it could only have been, rather than as a success with a stray first line.
    /// </summary>
    [Test]
    public async Task OutputWithNoStatusLineIsAFailure()
    {
        using var path = new TempFile();
        await File.WriteAllTextAsync(path, "Tool 'buildmonitor' failed to update due to the following:\nIt did not work.\n");
        await Assert.That(UpdateOutcome.Take(path)).IsEqualTo(new("Update failed", "It did not work."));
    }

    [Test]
    public async Task NoFileMeansNothingToReport() =>
        await Assert.That(UpdateOutcome.Take("fake")).IsNull();

    /// <summary>
    /// The first line only says that the update failed. The Windows line endings and the blank line
    /// at the end are what the shells leave.
    /// </summary>
    [Test]
    public async Task TheReasonIsTheLastLineWritten() =>
        await Assert.That(UpdateOutcome.Describe("Tool 'buildmonitor' failed to update due to the following:\r\nUnable to load the service index for source https://api.nuget.org/v3/index.json.\r\n\r\n").Message)
            .IsEqualTo("Unable to load the service index for source https://api.nuget.org/v3/index.json.");

    [Test]
    public async Task NoOutputStillSaysTheUpdateFailed() =>
        await Assert.That(UpdateOutcome.Describe(" \n")).IsEqualTo(new("Update failed", "dotnet tool update failed without saying why."));
}
