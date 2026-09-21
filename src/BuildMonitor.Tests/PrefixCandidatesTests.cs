public class PrefixCandidatesTests
{
    static string[] watched =
    [
        "Utilities.Logging.Client",
        "Utilities.Logging.Server",
        "Utilities.Storage",
        "TheProjectApi",
        "TheProjectUi",
        "DiffEngine"
    ];

    /// <summary>
    /// Longest first: the narrower group is the one a right click on that row was most likely
    /// about.
    /// </summary>
    [Test]
    public async Task EveryBoundaryAnotherProjectSharesLongestFirst() =>
        await Assert.That(PrefixCandidates.Of("Utilities.Logging.Client", watched, []))
            .IsEquivalentTo(["Utilities.Logging", "Utilities"]);

    /// <summary>
    /// A family with no separator in its names, which is the case the options page was the only
    /// way to describe. "The" is not offered after it: the same two rows under a worse name.
    /// </summary>
    [Test]
    public async Task ACamelCasedNameBreaksAtItsHumps() =>
        await Assert.That(PrefixCandidates.Of("TheProjectApi", watched, []))
            .IsEquivalentTo(["TheProject"]);

    /// <summary>
    /// A prefix of nothing but its own name would make a group of one, which draws as the plain
    /// row it already was.
    /// </summary>
    [Test]
    public async Task APrefixNoOtherProjectSharesIsNotOffered() =>
        await Assert.That(PrefixCandidates.Of("DiffEngine", watched, [])).IsEmpty();

    [Test]
    public async Task APrefixAlreadyGroupedByIsNotOffered() =>
        await Assert.That(PrefixCandidates.Of("Utilities.Logging.Client", watched, ["utilities.logging"]))
            .IsEquivalentTo(["Utilities"]);

    /// <summary>
    /// An acronym is a word: "APIGateway" breaks after the acronym rather than before its last
    /// letter, which would offer "AP".
    /// </summary>
    [Test]
    public async Task AnAcronymIsOneWord() =>
        await Assert.That(PrefixCandidates.Of("APIGateway", ["APIGateway", "APIRegistry"], []))
            .IsEquivalentTo(["API"]);

    /// <summary>
    /// A single letter groups half an account under an initial, and is as likely a typo as a
    /// choice.
    /// </summary>
    [Test]
    public async Task ASingleLetterIsNotAPrefix() =>
        await Assert.That(PrefixCandidates.Of("XClient", ["XClient", "XServer"], [])).IsEmpty();

    /// <summary>
    /// A deeply named project has a boundary every few characters, and every one of them is a
    /// prefix of the one before: past a handful they bury the items under them.
    /// </summary>
    [Test]
    public async Task NoMoreThanAMenuCanCarry()
    {
        // A project sharing one more level with each of five others, so every boundary in its name
        // reaches further than the last and all of them could be offered.
        var deep = "AA.BB.CC.DD.EE.FF";
        string[] family =
        [
            deep,
            "AA.BB.CC.DD.EE.XX",
            "AA.BB.CC.DD.YY",
            "AA.BB.CC.ZZ",
            "AA.BB.WW",
            "AA.VV"
        ];
        await Assert.That(PrefixCandidates.Of(deep, family, []))
            .IsEquivalentTo(["AA.BB.CC.DD.EE", "AA.BB.CC.DD", "AA.BB.CC", "AA.BB"]);
    }
}
