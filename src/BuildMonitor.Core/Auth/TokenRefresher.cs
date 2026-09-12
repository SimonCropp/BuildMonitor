/// <summary>
/// Trades a stored refresh token for a new access token when a poll is refused. One attempt
/// per refusal; a refresh that fails means the user has to sign in again.
/// </summary>
sealed class TokenRefresher(ISecretStore secrets, HttpMessageHandler handler)
{
    public async Task<bool> TryRefresh(Connection connection, Cancel cancel)
    {
        var refresh = secrets.Read(SecretKeys.Refresh(connection.Id));
        if (refresh is null ||
            OAuthClients.For(connection) is not { } client)
        {
            return false;
        }

        try
        {
            var response = await OAuthFlows.Refresh(client, refresh, handler, cancel);
            if (response.AccessToken is null)
            {
                return false;
            }

            secrets.Write(SecretKeys.Token(connection.Id), response.AccessToken);
            if (response.RefreshToken is not null)
            {
                secrets.Write(SecretKeys.Refresh(connection.Id), response.RefreshToken);
            }

            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            Log.Warning(exception, "Refreshing the token for {Connection} failed", connection.Name);
            return false;
        }
    }
}
