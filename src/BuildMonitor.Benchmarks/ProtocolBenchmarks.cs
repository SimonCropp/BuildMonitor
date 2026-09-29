/// <summary>
/// What an MCP tool costs end to end, bar the socket: the message built and parsed, the tray's
/// answer serialized, built and parsed, and the caller's objects read out of it. The listing is
/// the large account's 2,940 builds; a build is its last.
/// </summary>
[MemoryDiagnoser]
public class ProtocolBenchmarks
{
    MonitorTools tools = new(new WireClient(Handler()));
    string buildKey = LargeAccount.Builds()[^1].Key;

    [Benchmark]
    public async Task<object> ListBuilds() =>
        await tools.ListBuilds(null, Cancel.None);

    [Benchmark]
    public async Task<object> GetBuild() =>
        await tools.GetBuild(buildKey, Cancel.None);

    static MessageHandler Handler()
    {
        var host = new SessionHost(LargeAccount.State());
        var poller = new Poller(host, new MemorySecretStore(), new(), new HttpClientHandler());
        return new(
            host,
            poller,
            _ =>
            {
            },
            _ =>
            {
            },
            new(),
            () => LargeAccount.Now);
    }
}
