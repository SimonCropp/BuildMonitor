public class PkceTests
{
    [Test]
    public async Task VerifierIsUrlSafeAndLongEnough()
    {
        var (verifier, challenge) = Pkce.Create();
        await Assert.That(verifier.Length).IsEqualTo(43);
        await Assert.That(verifier.All(_ => char.IsAsciiLetterOrDigit(_) || _ is '-' or '_')).IsTrue();
        await Assert.That(challenge.Length).IsEqualTo(43);
    }

    [Test]
    public async Task KnownVector()
    {
        // RFC 7636 appendix B.
        var challenge = Pkce.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk");
        await Assert.That(challenge).IsEqualTo("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM");
    }
}
