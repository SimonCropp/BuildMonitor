/// <summary>
/// Which failures a channel announces. Each is told once, and only from a connection the tray had
/// polled by the look before, so a tray just started or restarted does not announce every failure
/// the account already had.
/// </summary>
public class FailureWatchTests
{
    static DateTimeOffset finished = Fixtures.Now;

    static BuildDto Failed(string key, string run, DateTimeOffset? at = null, string connection = "GitHub") =>
        new(
            key,
            connection,
            "test.yml",
            "VerifyTests/Verify",
            "main",
            run,
            nameof(BuildStatus.Failed),
            null,
            null,
            at ?? finished,
            -1,
            "",
            "",
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            false);

    static List<ConnectionDto> Polled(params string[] names) =>
        [.. names.Select(_ => new ConnectionDto(_, _, "GitHub Actions", "Ok", null, Fixtures.Now, 1))];

    static List<ConnectionDto> Unpolled(params string[] names) =>
        [.. names.Select(_ => new ConnectionDto(_, _, "GitHub Actions", "Unknown", null, null, 0))];

    [Test]
    public async Task AFailureIsNewsOnceAfterTheFirstLook()
    {
        var watch = new FailureWatch();
        await Assert.That(watch.Next([Failed("a", "1")], Polled("GitHub"))).IsEmpty();

        var news = watch.Next([Failed("a", "1"), Failed("b", "7")], Polled("GitHub"));
        await Assert.That(news.Select(_ => _.Key)).IsEquivalentTo(["b"]);
        await Assert.That(watch.Next([Failed("a", "1"), Failed("b", "7")], Polled("GitHub"))).IsEmpty();
    }

    /// <summary>
    /// A new run failing, or the same run failing again at another time, as a retry under the same
    /// number does, is news; the same failure seen again is not.
    /// </summary>
    [Test]
    public async Task ARetryThatFailsAgainIsNews()
    {
        var watch = new FailureWatch();
        watch.Next([Failed("a", "1")], Polled("GitHub"));
        var news = watch.Next([Failed("a", "1", finished.AddMinutes(5))], Polled("GitHub"));
        await Assert.That(news.Count).IsEqualTo(1);
    }

    /// <summary>
    /// The tray this server started, before its first poll, lists nothing, and its first poll lists
    /// every failure the account has. None of them is news.
    /// </summary>
    [Test]
    public async Task AConnectionsFirstPollIsNotNews()
    {
        var watch = new FailureWatch();
        await Assert.That(watch.Next([], Unpolled("GitHub"))).IsEmpty();
        await Assert.That(watch.Next([Failed("a", "1"), Failed("b", "2")], Polled("GitHub"))).IsEmpty();
        await Assert.That(watch.Next([Failed("a", "1"), Failed("b", "2"), Failed("c", "3")], Polled("GitHub")).Select(_ => _.Key))
            .IsEquivalentTo(["c"]);
    }

    /// <summary>
    /// A tray restarting answers with nothing, then lists again the failures it had. They were seen
    /// before it restarted, so they are not news when they come back.
    /// </summary>
    [Test]
    public async Task ATrayRestartingDoesNotRetellItsFailures()
    {
        var watch = new FailureWatch();
        watch.Next([Failed("a", "1")], Polled("GitHub"));
        watch.Next([Failed("a", "1")], Polled("GitHub"));
        await Assert.That(watch.Next([], Unpolled("GitHub"))).IsEmpty();
        await Assert.That(watch.Next([Failed("a", "1")], Polled("GitHub"))).IsEmpty();
    }

    /// <summary>
    /// One connection's first poll is not news while another's failures are.
    /// </summary>
    [Test]
    public async Task EachConnectionIsTakenOnItsOwn()
    {
        var watch = new FailureWatch();
        watch.Next([], [.. Polled("GitHub"), .. Unpolled("Jenkins")]);
        var news = watch.Next([Failed("a", "1"), Failed("j", "5", connection: "Jenkins")], Polled("GitHub", "Jenkins"));
        await Assert.That(news.Select(_ => _.Key)).IsEquivalentTo(["a"]);
    }
}
