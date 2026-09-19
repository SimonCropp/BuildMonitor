public class ArtifactPlanTests
{
    static BuildArtifact Artifact(string name, long? bytes = 1024, string? unavailable = null) =>
        new(name, name, bytes, unavailable);

    /// <summary>
    /// The order an assistant reads them in, so the band rule is one readable list rather than a
    /// set of assertions that each pass for the wrong reason.
    /// </summary>
    [Test]
    public Task TheMostUsefulFilesComeFirst() =>
        Verify(ArtifactPlan.Order(
            [
                Artifact("app.nupkg"),
                Artifact("coverage.cobertura.xml"),
                Artifact("screenshots/failure.png"),
                Artifact("build.binlog"),
                Artifact("Snapshot.received.txt"),
                Artifact("test-results.trx"),
                Artifact("something-else.dat"),
                Artifact("installer.msi")
            ])
            .Select(_ => _.Name))
        .Snapshot(
            """
            [
              test-results.trx,
              build.binlog,
              Snapshot.received.txt,
              screenshots/failure.png,
              coverage.cobertura.xml,
              something-else.dat,
              app.nupkg,
              installer.msi
            ]
            """);

    /// <summary>
    /// Every GitHub artifact arrives as a zip whatever was uploaded, so demoting the extension
    /// would push a whole service behind every other file.
    /// </summary>
    [Test]
    public async Task AZipIsNotTreatedAsAPayload()
    {
        var order = ArtifactPlan.Order([Artifact("payload.exe"), Artifact("results.zip")]);
        await Assert.That(order.Select(_ => _.Name)).IsEquivalentTo(["results.zip", "payload.exe"]);
    }

    [Test]
    public async Task TheSmallestOfABandIsTakenFirst()
    {
        var order = ArtifactPlan.Order([Artifact("big.trx", 9000), Artifact("small.trx", 10)]);
        await Assert.That(order.Select(_ => _.Name)).IsEquivalentTo(["small.trx", "big.trx"]);
    }

    /// <summary>
    /// An unknown size cannot be weighed against the others, so it follows the ones that can be
    /// rather than sorting as though it were empty and taking the front of its band.
    /// </summary>
    [Test]
    public async Task AnUnknownSizeFollowsTheKnownOnesInItsBand()
    {
        var order = ArtifactPlan.Order([Artifact("unknown.trx", null), Artifact("known.trx", 9000)]);
        await Assert.That(order.Select(_ => _.Name)).IsEquivalentTo(["known.trx", "unknown.trx"]);
    }

    /// <summary>
    /// The reasons are what the user and the assistant read, so they are pinned as the text they
    /// are rather than asserted a field at a time.
    /// </summary>
    [Test]
    public Task WhatIsLeftBehindAndWhy() =>
        Verify(ArtifactPlan.For(
                [
                    Artifact("test-results.trx", 2048),
                    Artifact("runner-image.tar", 2254857830),
                    Artifact("gone.zip", 4096, "expired"),
                    Artifact("dumps.zip", 40 * 1024 * 1024),
                    Artifact("app.log", 30 * 1024 * 1024),
                    Artifact("coverage.zip", 25 * 1024 * 1024)
                ],
                budget: 50 * 1024 * 1024,
                perFile: 35 * 1024 * 1024)
            .Skip)
        .Snapshot(
            """
            [
              {
                Name: dumps.zip,
                Bytes: 41943040,
                Reason: 40 MB, over the 35 MB limit for one file
              },
              {
                Name: coverage.zip,
                Bytes: 26214400,
                Reason: 25 MB, and only 20 MB of the 50 MB budget was left
              },
              {
                Name: gone.zip,
                Bytes: 4096,
                Reason: expired
              },
              {
                Name: runner-image.tar,
                Bytes: 2254857830,
                Reason: 2.1 GB, over the 35 MB limit for one file
              }
            ]
            """);

    [Test]
    public async Task AnExpiredArtifactIsNeverPlannedAndKeepsTheServicesWord()
    {
        var plan = ArtifactPlan.For([Artifact("gone.zip", 10, "expired")]);
        await Assert.That(plan.Take).IsEmpty();
        await Assert.That(plan.Skip.Single().Reason).IsEqualTo("expired");
    }

    [Test]
    public async Task TheBudgetStopsTakingOnceItIsSpent()
    {
        var plan = ArtifactPlan.For(
            [Artifact("one.trx", 600), Artifact("two.trx", 300), Artifact("three.trx", 200)],
            budget: 1000,
            perFile: 1000);
        // Smallest first, so 200 and 300 fit and the 600 does not.
        await Assert.That(plan.Take.Select(_ => _.Name)).IsEquivalentTo(["three.trx", "two.trx"]);
        await Assert.That(plan.Skip.Single().Name).IsEqualTo("one.trx");
    }

    [Test]
    public async Task TheCountLimitStopsTakingHoweverSmallTheFilesAre()
    {
        var plan = ArtifactPlan.For(
            Enumerable.Range(0, 5).Select(_ => Artifact($"file{_}.trx", 1)),
            count: 3);
        await Assert.That(plan.Take.Length).IsEqualTo(3);
        await Assert.That(plan.Skip.Select(_ => _.Reason).Distinct().Single()).IsEqualTo("the 3 file limit was reached");
    }

    /// <summary>
    /// Jenkins declares no artifact size at all. Skipping every unsized artifact would leave that
    /// service with nothing, so they are taken and held to the per file cap while copying.
    /// </summary>
    [Test]
    public async Task AnArtifactWithNoDeclaredSizeIsStillTaken()
    {
        var plan = ArtifactPlan.For([Artifact("console.log", null)], budget: 10, perFile: 10);
        await Assert.That(plan.Take.Single().Name).IsEqualTo("console.log");
        await Assert.That(plan.Skip).IsEmpty();
    }

    [Test]
    [Arguments(100L, 20L, 20L)]
    [Arguments(10L, 20L, 10L)]
    [Arguments(0L, 20L, 0L)]
    [Arguments(-5L, 20L, 0L)]
    public async Task TheLimitForOneFetchIsWhicheverIsSmaller(long left, long perFile, long expected) =>
        await Assert.That(ArtifactPlan.Limit(left, perFile)).IsEqualTo(expected);

    [Test]
    public async Task NoArtifactsPlansNothing()
    {
        var plan = ArtifactPlan.For([]);
        await Assert.That(plan.Take).IsEmpty();
        await Assert.That(plan.Skip).IsEmpty();
    }
}
