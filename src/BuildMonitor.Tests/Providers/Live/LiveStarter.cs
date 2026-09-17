/// <summary>
/// Starts a run of the sandbox the way a user would: Retry on the newest build that offers it.
/// Octopus offers no retry, so a new deployment stands in there.
/// </summary>
static class LiveStarter
{
    /// <summary>
    /// Starts a run. With nothing to retry, <paramref name="skipWhenNone"/> skips: a sandbox never
    /// run by hand has nothing to start from. Otherwise it fails: a round that left no retryable
    /// build has broken the next one.
    /// </summary>
    public static async Task Start(LiveConnection live, ProviderContext context, Pipeline sandbox, IReadOnlyList<Build> builds, bool skipWhenNone, Cancel cancel)
    {
        if (live.Id == "octopus")
        {
            await OctopusStarter.Deploy(live, context, sandbox, cancel);
            return;
        }

        var target = builds.FirstOrDefault(_ => _.Retryable());
        if (target is null)
        {
            var message = $"{live.Id}: the sandbox has no failed or cancelled build to retry ({LiveLog.Rows(builds)}). Run it once by hand.";
            if (skipWhenNone)
            {
                Skip.Test(message);
            }

            throw new InvalidOperationException(message);
        }

        LiveLog.Line($"{live.Id}: retrying {LiveLog.Row(target)}");
        await live.Provider.Retry(context, target, cancel);
    }
}
