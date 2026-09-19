/// <param name="TimedOut">Set when the flow ran out of time with the provider still waiting, rather
/// than the provider answering. Kept apart from an ordinary failure because it is the one outcome
/// that says nothing about why: whatever the provider put on its own page, an address it refused
/// included, never reached BuildMonitor.</param>
record AuthResult(bool Ok, string? AccessToken, string? RefreshToken, string? Error, bool TimedOut = false)
{
    public static AuthResult Success(string accessToken, string? refreshToken) =>
        new(true, accessToken, refreshToken, null);

    public static AuthResult Failed(string error) =>
        new(false, null, null, error);

    public static AuthResult Expired(string error) =>
        new(false, null, null, error, TimedOut: true);
}
