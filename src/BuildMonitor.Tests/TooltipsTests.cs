public class TooltipsTests
{
    [Test]
    public async Task ShortTextIsLeftAlone() =>
        await Assert.That(Tooltips.Wrap("Open branch: main")).IsEqualTo("Open branch: main");

    [Test]
    public async Task BreaksBeforeTheWordThatWouldRunPastTheMeasure() =>
        await Assert.That(Tooltips.Wrap("Download artifacts and log, and copy a prompt naming both"))
            .IsEqualTo(
                """
                Download artifacts and log, and copy a prompt
                naming both
                """);

    /// <summary>
    /// A checkout path is one word and longer than the measure, so there is nowhere to break it
    /// that would not read as two paths.
    /// </summary>
    [Test]
    public async Task AWordLongerThanTheMeasureKeepsALineToItself() =>
        await Assert.That(Tooltips.Wrap(@"Open folder: C:\Code\BuildMonitor\src\BuildMonitor.Core"))
            .IsEqualTo(
                """
                Open folder:
                C:\Code\BuildMonitor\src\BuildMonitor.Core
                """);

    /// <summary>
    /// The lines a tooltip already has mean more than these do: a row's summary is one thing to a
    /// line, and the status is one failing connection to a line.
    /// </summary>
    [Test]
    public async Task LinesItAlreadyHasAreKeptAndWrappedInTurn() =>
        await Assert.That(Tooltips.Wrap("GitHub: rate limited, retrying in 4m\nJenkins: the server did not answer in time, and is not answering now"))
            .IsEqualTo(
                """
                GitHub: rate limited, retrying in 4m
                Jenkins: the server did not answer in time, and
                is not answering now
                """);
}
