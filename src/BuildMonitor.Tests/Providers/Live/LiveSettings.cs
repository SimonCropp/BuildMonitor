using Microsoft.Extensions.Configuration;

/// <summary>
/// Where the live tests read their settings. In order: the environment, then the file a local
/// Docker server's provision.sh writes, then user secrets.
/// <para>
/// A GitHub secret that is not set reaches a job as an empty string, not as a missing variable. So
/// a blank value counts as unset everywhere. Otherwise a provider with no sandbox would run with
/// an empty token and fail instead of skipping.
/// </para>
/// <para>
/// ProjectDefaults turns GenerateAssemblyInfo off for test projects. A UserSecretsId in the
/// project file would therefore never reach the assembly, and AddUserSecrets would read nothing
/// without saying so. The id is passed directly instead, and the docs set secrets with
/// <c>dotnet user-secrets set NAME VALUE --id BuildMonitor.LiveTests</c>.
/// </para>
/// </summary>
static class LiveSettings
{
    public const string UserSecretsId = "BuildMonitor.LiveTests";

    /// <summary>
    /// The line every sandbox pipeline prints before it fails. Finding it in a fetched log proves
    /// the log is the sandbox's, not another job's or an earlier attempt's.
    /// </summary>
    public const string Marker = "BuildMonitor live test";

    public const int ReadTimeout = 10 * 60 * 1000;

    public const int PollTimeout = 15 * 60 * 1000;

    public const int ActionTimeout = 60 * 60 * 1000;

    static string sourceDirectory = SourceDirectory();

    static Lazy<IConfigurationProvider[]> sources = new(() => [..Build().Providers.Reverse()]);

    static Lazy<ImmutableHashSet<string>?> named = new(ParseProviders);

    /// <summary>
    /// The test cases: every provider, or those BUILDMONITOR_LIVE_PROVIDERS names. A workflow job
    /// runs one provider, and a skipped case for each of the others would bury its output.
    /// </summary>
    public static IEnumerable<string> ProviderIds() =>
        AllIds().Where(Selected);

    static IEnumerable<string> AllIds() =>
        ProviderDescriptors.All.Select(_ => _.Id);

    public static string Prefix(string providerId) =>
        $"BUILDMONITOR_{providerId.ToUpperInvariant().Replace('-', '_')}_";

    /// <summary>
    /// The first value that is not blank, from the most specific source down.
    /// </summary>
    public static string? Value(string name)
    {
        foreach (var source in sources.Value)
        {
            if (source.TryGet(name, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// Whether BUILDMONITOR_LIVE_PROVIDERS, when set, includes the provider. Unset or <c>all</c>
    /// includes every provider.
    /// </summary>
    public static bool Selected(string providerId)
    {
        if (named.Value is not { } providers)
        {
            return true;
        }

        return providers.Contains(providerId);
    }

    /// <summary>
    /// Whether BUILDMONITOR_LIVE_PROVIDERS names the provider itself, as every workflow job does.
    /// Such a provider fails when a setting is missing, rather than skipping and passing green.
    /// </summary>
    public static bool Named(string providerId) =>
        named.Value?.Contains(providerId) == true;

    public static bool Actions =>
        Value("BUILDMONITOR_LIVE_ACTIONS") is { } value &&
        (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1");

    /// <summary>
    /// Whether this run's output is public. The repository is public, so a GitHub Actions log is
    /// too.
    /// </summary>
    public static bool Public =>
        Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";

    /// <summary>
    /// How long a started build may wait for a runner. AppVeyor runs one job at a time, so this
    /// can be raised with BUILDMONITOR_LIVE_QUEUE_MINUTES.
    /// </summary>
    public static TimeSpan QueueTimeout
    {
        get
        {
            if (int.TryParse(Value("BUILDMONITOR_LIVE_QUEUE_MINUTES"), out var minutes) &&
                minutes > 0)
            {
                return TimeSpan.FromMinutes(minutes);
            }

            return TimeSpan.FromMinutes(15);
        }
    }

    static ImmutableHashSet<string>? ParseProviders()
    {
        var value = Value("BUILDMONITOR_LIVE_PROVIDERS");
        if (value is null ||
            value.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var ids = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(_ => _.ToLowerInvariant())
            .ToImmutableHashSet();
        var unknown = ids.Where(_ => ProviderDescriptors.All.All(descriptor => descriptor.Id != _)).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException($"BUILDMONITOR_LIVE_PROVIDERS names unknown providers: {string.Join(", ", unknown)}. Known: all, {string.Join(", ", AllIds())}");
        }

        return ids;
    }

    static IConfigurationRoot Build()
    {
        var builder = new ConfigurationBuilder()
            .AddUserSecrets(UserSecretsId);
        var state = Path.Combine(sourceDirectory, "Servers", ".state");
        if (Directory.Exists(state))
        {
            builder.AddInMemoryCollection(ServerSettings(state));
        }

        return builder
            .AddEnvironmentVariables()
            .Build();
    }

    /// <summary>
    /// The NAME=VALUE lines each local server's provision.sh wrote. A local run then needs no
    /// environment set up for a server started in Docker. provision.sh down deletes the file.
    /// </summary>
    static IEnumerable<KeyValuePair<string, string?>> ServerSettings(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.env"))
        {
            foreach (var line in File.ReadLines(file))
            {
                var index = line.IndexOf('=');
                if (index > 0)
                {
                    yield return new(line[..index].Trim(), line[(index + 1)..].Trim());
                }
            }
        }
    }

    static string SourceDirectory([CallerFilePath] string path = "") =>
        Path.GetDirectoryName(path)!;
}
