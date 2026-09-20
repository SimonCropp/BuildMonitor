/// <summary>
/// Retries, cancels, and where the service can reorder its queue moves, real builds of one sandbox
/// pipeline per provider, named by
/// <c>BUILDMONITOR_{PROVIDER}_PIPELINE</c>. Two opt-ins guard it, because it spends the
/// sandbox's build minutes and changes what the account shows. The class is explicit, and it also
/// needs <c>BUILDMONITOR_LIVE_ACTIONS=true</c>. A filter meant for the read tests alone then never
/// starts a build.
/// </summary>
[Explicit]
public class LiveActionTests
{
    [Test]
    [MethodDataSource(typeof(LiveSettings), nameof(LiveSettings.ProviderIds))]
    [Timeout(LiveSettings.ActionTimeout)]
    public Task RetryCancelRetry(string providerId, Cancel cancel)
    {
        if (!LiveSettings.Actions)
        {
            Skip.Test("Set BUILDMONITOR_LIVE_ACTIONS=true to retry and cancel sandbox builds.");
        }

        var live = LiveConnection.Require(providerId, actions: true);
        return LiveRound.Run(live, cancel);
    }
}
