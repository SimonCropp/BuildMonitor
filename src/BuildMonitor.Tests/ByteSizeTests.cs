public class ByteSizeTests
{
    [Test]
    [Arguments(0L, "0 B")]
    [Arguments(1L, "1 B")]
    [Arguments(1023L, "1023 B")]
    [Arguments(1024L, "1 KB")]
    [Arguments(1536L, "1.5 KB")]
    [Arguments(10240L, "10 KB")]
    [Arguments(1048576L, "1 MB")]
    [Arguments(39845888L, "38 MB")]
    [Arguments(356515840L, "340 MB")]
    [Arguments(2254857830L, "2.1 GB")]
    [Arguments(1099511627776L, "1 TB")]
    public async Task SizesReadTheWayTheyAreWritten(long bytes, string expected) =>
        await Assert.That(ByteSize.Human(bytes)).IsEqualTo(expected);

    /// <summary>
    /// Sizes arrive from services as numbers this code did not choose, and a negative one should
    /// read as the absence it is rather than as a file of some impossible size.
    /// </summary>
    [Test]
    public async Task ASizeThatCannotBeOneSaysSo() =>
        await Assert.That(ByteSize.Human(-1)).IsEqualTo("unknown size");

    /// <summary>
    /// The largest unit runs out before long does, and the count has to keep climbing rather than
    /// wrap into a smaller one.
    /// </summary>
    [Test]
    public async Task PastTheLargestUnitTheNumberGrows() =>
        await Assert.That(ByteSize.Human(long.MaxValue)).IsEqualTo("8388608 TB");
}
