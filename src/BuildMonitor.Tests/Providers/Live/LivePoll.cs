/// <summary>
/// Reads a sandbox until its builds reach a state, the way the action round follows a retry or a
/// cancel. A real service hiccups: a read that times out, a 502 or a rate limit is waited out, not
/// taken as the provider being wrong.
/// </summary>
static class LivePoll
{
    const int transientLimit = 3;

    /// <summary>
    /// The builds once <paramref name="done"/> accepts them. On timeout, the failure names what was
    /// awaited and the last state seen, which is most of what is needed to tell a slow queue from a
    /// status the provider maps wrong.
    /// </summary>
    public static async Task<IReadOnlyList<Build>> Until(
        string providerId,
        string awaited,
        Func<Cancel, Task<IReadOnlyList<Build>>> read,
        Func<IReadOnlyList<Build>, bool> done,
        TimeSpan timeout,
        TimeSpan interval,
        Cancel cancel)
    {
        using var deadline = CancelSource.CreateLinkedTokenSource(cancel);
        deadline.CancelAfter(timeout);
        var watch = Stopwatch.StartNew();
        string? last = null;
        var failures = 0;
        LiveLog.Line($"{providerId}: waiting for {awaited}");
        while (true)
        {
            var wait = interval;
            try
            {
                var builds = await read(deadline.Token);
                failures = 0;
                var seen = LiveLog.Rows(builds);
                if (seen != last)
                {
                    LiveLog.Line($"{providerId}: {watch.Elapsed:mm\\:ss} {seen}");
                    last = seen;
                }

                if (done(builds))
                {
                    return builds;
                }
            }
            catch (RateLimitException exception)
            {
                wait = exception.RetryAfter ?? TimeSpan.FromMinutes(1);
                LiveLog.Line($"{providerId}: rate limited, waiting {wait}");
            }
            catch (Exception exception) when (!deadline.IsCancellationRequested &&
                                              Transient(exception))
            {
                failures++;
                if (failures == transientLimit)
                {
                    throw;
                }

                LiveLog.Line($"{providerId}: a read failed ({Describe(exception)}), trying again");
            }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
            {
                throw TimedOut();
            }

            try
            {
                await Task.Delay(wait, deadline.Token);
            }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
            {
                throw TimedOut();
            }
        }

        TimeoutException TimedOut() =>
            new($"{providerId}: {awaited} did not happen within {timeout}. Last seen: {last ?? "nothing"}");
    }

    /// <summary>
    /// A failure worth a second read: the connection, a gateway, or HttpClient's own timeout, which
    /// arrives as a cancellation the test did not ask for.
    /// </summary>
    static bool Transient(Exception exception) =>
        exception switch
        {
            HttpRequestException { StatusCode: null } => true,
            HttpRequestException { StatusCode: { } status } => (int) status >= 500,
            IOException or TaskCanceledException => true,
            _ => false
        };

    static string Describe(Exception exception)
    {
        if (exception is HttpRequestException { StatusCode: { } status })
        {
            return $"{(int) status}";
        }

        return exception.GetType().Name;
    }
}
