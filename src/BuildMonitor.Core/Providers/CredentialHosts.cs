/// <summary>
/// Whether the credential on a request may follow a redirect to another host.
/// <para>
/// The transport's own redirect following drops the Authorization header the moment a redirect
/// leaves the host it was sent to, which is the right default: most services hand a download off to
/// storage with a signature already in the URL, and a blob store refuses a credential it does not
/// know. Azure DevOps is the exception. It serves an artifact by redirecting from the organization's
/// address to a separate artifacts host that still wants the personal access token, so every
/// artifact request arrived there anonymous and was answered with a sign in page.
/// </para>
/// <para>
/// An allow list rather than a rule about shared parent domains, because being wrong here sends the
/// user's token to a host that should never see it. A service not named here keeps the transport's
/// behaviour.
/// </para>
/// </summary>
static class CredentialHosts
{
    /// <summary>
    /// Azure DevOps, which spreads one credential over several hosts: the organization's address,
    /// the artifacts hosts (<c>artprodeus21.artifacts.visualstudio.com</c> and its regional
    /// siblings), the pipeline artifact blob hosts under <c>vsassets.io</c>, and the older
    /// <c>{organization}.visualstudio.com</c> addresses that still answer. Azure DevOps Server keeps
    /// all of it on one host, which the same host rule already covers.
    /// </summary>
    static readonly string[] azureDevOps =
    [
        "dev.azure.com",
        "visualstudio.com",
        "vsassets.io"
    ];

    /// <summary>
    /// True when the credential may be sent to <paramref name="to"/> after a request to
    /// <paramref name="from"/> was redirected there: the same host, or two hosts of the one service.
    /// Both ends must be in the same family, so a redirect that leaves a service never carries the
    /// credential, however well known the host it points at.
    /// </summary>
    public static bool Follows(Uri from, Uri to)
    {
        if (string.Equals(from.Host, to.Host, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return InFamily(from.Host, azureDevOps) &&
               InFamily(to.Host, azureDevOps);
    }

    /// <summary>
    /// Matched on a label boundary rather than as a suffix, so <c>notvisualstudio.com</c> is not
    /// read as a host of the service <c>visualstudio.com</c> names.
    /// </summary>
    static bool InFamily(string host, string[] family) =>
        family.Any(_ => string.Equals(host, _, StringComparison.OrdinalIgnoreCase) ||
                        host.EndsWith($".{_}", StringComparison.OrdinalIgnoreCase));
}
