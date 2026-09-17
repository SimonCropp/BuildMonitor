/// <summary>
/// One provider's connection, as the live tests build it from the BUILDMONITOR_{ID}_* settings.
/// The connection's id and name are the provider's, so nothing printed about the connection
/// itself identifies an account.
/// </summary>
sealed class LiveConnection
{
    /// <summary>
    /// One handler for the whole run, configured as MonitorProgram configures the app's. Redirects,
    /// decompression and connection reuse then behave as they do for a user. A fake answers each
    /// request as it was sent, so only a real handler shows what following a redirect does to it.
    /// </summary>
    static SocketsHttpHandler handler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = DecompressionMethods.All
    };

    LiveConnection(IProvider provider, Connection connection, string token, string? pipeline, string? deployEnvironment, BuildAccess? expectedAccess)
    {
        Provider = provider;
        Connection = connection;
        Token = token;
        Pipeline = pipeline;
        DeployEnvironment = deployEnvironment;
        ExpectedAccess = expectedAccess;
    }

    public static HttpMessageHandler Handler => handler;

    public IProvider Provider { get; }

    public ProviderDescriptor Descriptor => Provider.Descriptor;

    public string Id => Descriptor.Id;

    public Connection Connection { get; }

    public string Token { get; }

    /// <summary>
    /// The sandbox pipeline's id or name, from _PIPELINE.
    /// </summary>
    public string? Pipeline { get; }

    /// <summary>
    /// The Octopus environment a sandbox deployment goes to, from _ENVIRONMENT.
    /// </summary>
    public string? DeployEnvironment { get; }

    /// <summary>
    /// What the token should be allowed to do, from _ACCESS, so a token that lost its write scope
    /// fails the read tests instead of the next action round.
    /// </summary>
    public BuildAccess? ExpectedAccess { get; }

    /// <summary>
    /// The provider's connection, or a skip that names every missing setting. A provider named in
    /// BUILDMONITOR_LIVE_PROVIDERS fails instead: a workflow job skipped for a renamed secret
    /// would otherwise pass green.
    /// </summary>
    public static LiveConnection Require(string providerId, bool actions = false)
    {
        if (!LiveSettings.Selected(providerId))
        {
            Skip.Test($"BUILDMONITOR_LIVE_PROVIDERS does not include {providerId}.");
        }

        var descriptor = ProviderDescriptors.Get(providerId);
        var prefix = LiveSettings.Prefix(providerId);
        var missing = new List<string>();

        string? Read(string suffix, bool required)
        {
            var value = LiveSettings.Value(prefix + suffix);
            if (value is null && required)
            {
                missing.Add(prefix + suffix);
            }

            return value;
        }

        var token = Read("TOKEN", true);
        var server = Read("SERVER", descriptor.DefaultServer is null);
        var user = Read("USER", descriptor.UserLabel is not null);
        var scope = ImmutableDictionary.CreateBuilder<string, string>();
        foreach (var field in descriptor.Scopes)
        {
            if (Read($"SCOPE_{field.Id.ToUpperInvariant()}", field.Required) is { } value)
            {
                scope[field.Id] = value;
            }
        }

        var pipeline = Read("PIPELINE", actions);
        var environment = Read("ENVIRONMENT", false);
        var auth = Parse<AuthMethod>(prefix + "AUTH", Read("AUTH", false)) ?? AuthMethod.Token;
        var access = Parse<BuildAccess>(prefix + "ACCESS", Read("ACCESS", false));
        if (token is null ||
            missing.Count > 0)
        {
            var message = $"{providerId} is not set up: {string.Join(", ", missing)} not set. See docs/live-tests.md.";
            if (LiveSettings.Named(providerId))
            {
                throw new InvalidOperationException(message);
            }

            Skip.Test(message);
        }

        var connection = new Connection
        {
            Id = providerId,
            ProviderId = providerId,
            Name = descriptor.Name,
            Server = server,
            User = user,
            Auth = auth,
            Scope = scope.ToImmutable()
        };
        return new(Providers.Get(providerId), connection, token, pipeline, environment, access);
    }

    /// <summary>
    /// A context as the poller builds one: the same memory for every call of a run, and the access
    /// the token was found to have. A context without the memory forgets Octopus's cancel grants
    /// and GitHub's read-only repositories between discovery and the fetch that needs them.
    /// </summary>
    public ProviderContext Context(ProviderMemory memory, ETagCache? cache = null, HttpMessageHandler? through = null, BuildAccess access = BuildAccess.Unknown) =>
        Providers.Context(Connection, Token, through ?? handler, cache) with
        {
            Memory = memory,
            Access = access
        };

    static T? Parse<T>(string name, string? value)
        where T : struct, Enum
    {
        if (value is null)
        {
            return null;
        }

        if (Enum.TryParse<T>(value, true, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"{name} is '{value}'. Expected one of: {string.Join(", ", Enum.GetNames<T>())}");
    }
}
