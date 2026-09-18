/// <summary>
/// The report a failed update leaves for the tray it starts again.
/// </summary>
public class FailedUpdateTests
{
    /// <summary>
    /// What dotnet tool update wrote when a running MCP server held the old version's files.
    /// </summary>
    const string locked =
        """
        Tool 'buildmonitor' failed to update due to the following:
        Failed to uninstall tool package 'buildmonitor': Access to the path 'C:\Users\me\.dotnet\tools\.store\buildmonitor\0.1.0-beta.6' is denied.
        """;

    [Test]
    public async Task TheFailureIsReportedOnceAndThenRemoved()
    {
        var path = TempFile();
        // As Windows PowerShell's Set-Content -Encoding UTF8 writes it, with a byte order mark.
        File.WriteAllText(path, locked, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        try
        {
            var notification = FailedUpdate.Take(path);

            await Assert.That(notification).IsEqualTo(new("Update failed", """Failed to uninstall tool package 'buildmonitor': Access to the path 'C:\Users\me\.dotnet\tools\.store\buildmonitor\0.1.0-beta.6' is denied."""));
            await Assert.That(File.Exists(path)).IsFalse();
            await Assert.That(FailedUpdate.Take(path)).IsNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task NoFileMeansNothingToReport() =>
        await Assert.That(FailedUpdate.Take(TempFile())).IsNull();

    /// <summary>
    /// The first line only says that the update failed. The Windows line endings and the blank line
    /// at the end are what the shells leave.
    /// </summary>
    [Test]
    public async Task TheReasonIsTheLastLineWritten() =>
        await Assert.That(FailedUpdate.Describe("Tool 'buildmonitor' failed to update due to the following:\r\nUnable to load the service index for source https://api.nuget.org/v3/index.json.\r\n\r\n").Message)
            .IsEqualTo("Unable to load the service index for source https://api.nuget.org/v3/index.json.");

    [Test]
    public async Task NoOutputStillSaysTheUpdateFailed() =>
        await Assert.That(FailedUpdate.Describe(" \n")).IsEqualTo(new("Update failed", "dotnet tool update failed without saying why."));

    static string TempFile() =>
        Path.Combine(Path.GetTempPath(), $"BuildMonitorFailedUpdate_{Guid.NewGuid():N}.log");
}
