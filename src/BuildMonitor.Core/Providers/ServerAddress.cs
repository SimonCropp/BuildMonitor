/// <summary>
/// Reads a server as a user typed it. A bare host such as <c>octopus.example.com</c> would
/// otherwise fail as "Invalid URI: The format of the URI could not be determined", so a server
/// with no scheme is taken as https. The check is for <c>://</c> rather than
/// <see cref="Uri.TryCreate(string, UriKind, out Uri)"/>, which reads <c>localhost:8080</c> as
/// the scheme <c>localhost</c>.
/// </summary>
static class ServerAddress
{
    public static string Normalize(string server)
    {
        server = server.Trim().TrimEnd('/');
        if (server.Length == 0 ||
            server.Contains("://", StringComparison.Ordinal))
        {
            return server;
        }

        return $"https://{server}";
    }
}
