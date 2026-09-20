/// <summary>
/// Runs one request per item with a bounded number in flight. A provider that needs a call per
/// repository would otherwise take minutes over a few hundred of them; eight at a time is well
/// inside every service's abuse limits and an order of magnitude faster. Results keep the
/// input order, so output is deterministic whatever the network did.
/// </summary>
static class Concurrently
{
    public const int Limit = 8;

    public static async Task<TResult[]> Map<TItem, TResult>(
        IReadOnlyList<TItem> items,
        Func<TItem, Cancel, Task<TResult>> work,
        Cancel cancel,
        Action<PollProgress>? progress = null)
    {
        var results = new TResult[items.Count];
        using var gate = new SemaphoreSlim(Limit);
        var tasks = new Task[items.Count];
        var done = 0;
        progress?.Invoke(new(0, items.Count));
        for (var index = 0; index < items.Count; index++)
        {
            tasks[index] = Run(index);
        }

        await Task.WhenAll(tasks);
        return results;

        async Task Run(int slot)
        {
            await gate.WaitAsync(cancel);
            try
            {
                results[slot] = await work(items[slot], cancel);
            }
            finally
            {
                gate.Release();
            }

            progress?.Invoke(new(Interlocked.Increment(ref done), items.Count));
        }
    }

    /// <summary>
    /// Like <see cref="Map{TItem,TResult}"/>, but an item that throws does not fail the others.
    /// With Map, one repository the token could not see discarded the whole poll and backed every
    /// repository off; here each item reports its own outcome. Cancellation still ends everything.
    /// </summary>
    public static async Task<Settled<TResult>[]> Settle<TItem, TResult>(
        IReadOnlyList<TItem> items,
        int limit,
        Func<TItem, Cancel, Task<TResult>> work,
        Cancel cancel,
        Action<PollProgress>? progress = null)
    {
        var results = new Settled<TResult>[items.Count];
        using var gate = new SemaphoreSlim(Math.Max(1, limit));
        var tasks = new Task[items.Count];
        var done = 0;
        progress?.Invoke(new(0, items.Count));
        for (var index = 0; index < items.Count; index++)
        {
            tasks[index] = Run(index);
        }

        await Task.WhenAll(tasks);
        return results;

        async Task Run(int slot)
        {
            await gate.WaitAsync(cancel);
            try
            {
                results[slot] = new(await work(items[slot], cancel), null);
            }
            catch (Exception exception)
                when (exception is not OperationCanceledException || !cancel.IsCancellationRequested)
            {
                results[slot] = new(default, exception);
            }
            finally
            {
                gate.Release();
            }

            progress?.Invoke(new(Interlocked.Increment(ref done), items.Count));
        }
    }
}
