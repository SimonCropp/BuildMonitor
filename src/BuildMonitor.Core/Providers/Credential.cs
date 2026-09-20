/// <summary>
/// Puts the credential on the request in the shape each API wants.
/// </summary>
static class Credential
{
    /// <summary>
    /// Every header <see cref="Apply"/> can put the secret in, for <see cref="RedirectingHandler"/>
    /// to strip when a redirect leaves the service. Authorization is the only one the transport
    /// knew to drop; the other two are as secret and used to cross with the request.
    /// </summary>
    public static readonly string[] Headers =
    [
        "Authorization",
        "PRIVATE-TOKEN",
        "X-Octopus-ApiKey"
    ];

    public static void Apply(HttpRequestHeaders headers, AuthScheme scheme, string secret, string? user)
    {
        switch (scheme)
        {
            case AuthScheme.Bearer:
                headers.Authorization = new("Bearer", secret);
                break;
            case AuthScheme.Token:
                headers.Authorization = new("token", secret);
                break;
            case AuthScheme.BasicUserToken:
                headers.Authorization = new("Basic", Basic($"{user}:{secret}"));
                break;
            case AuthScheme.BasicEmptyUserToken:
                headers.Authorization = new("Basic", Basic($":{secret}"));
                break;
            case AuthScheme.BasicEmailToken:
                headers.Authorization = new("Basic", Basic($"{user}:{secret}"));
                break;
            case AuthScheme.HeaderPrivateToken:
                headers.Remove("PRIVATE-TOKEN");
                headers.TryAddWithoutValidation("PRIVATE-TOKEN", secret);
                break;
            case AuthScheme.HeaderOctopusApiKey:
                headers.Remove("X-Octopus-ApiKey");
                headers.TryAddWithoutValidation("X-Octopus-ApiKey", secret);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scheme), scheme, null);
        }
    }

    static string Basic(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
}
