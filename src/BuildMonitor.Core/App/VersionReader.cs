static class VersionReader
{
    /// <summary>
    /// Settable so tests can pin what the issue URL and the options page show.
    /// </summary>
    public static string VersionString = typeof(VersionReader).Assembly
        .GetCustomAttributes<AssemblyInformationalVersionAttribute>()
        .Single()
        .InformationalVersion;
}
