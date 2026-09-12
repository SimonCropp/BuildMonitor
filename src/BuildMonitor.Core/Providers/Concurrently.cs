/// <summary>
/// Runs one request per item with a bounded number in flight. A provider that needs a call per
/// repository would otherwise take minutes over a few hundred of them; eight at a time is well
/// inside every service's abuse limits and an order of magnitude faster. Results keep the
/// input order, so output is deterministic whatever the network did.
/// </summary>
static class Concurrently
{
    public const int Limit = 8;

    public static async Task<List<TResult>> Map<TItem, TResult>(
        IReadOnlyList<TItem> items,
        Func<TItem, Cancel, Task<TResult>> work,
        Cancel cancel)
    {
        var results = new TResult[items.Count];
        using var gate = new SemaphoreSlim(Limit);
        var tasks = new Task[items.Count];
        for (var index = 0; index < items.Count; index++)
        {
            var slot = index;
            tasks[index] = Run(slot);
        }

        await Task.WhenAll(tasks);
        return [..results];

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
        }
    }
}
