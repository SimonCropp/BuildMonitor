using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

/// <summary>
/// Hands back what a burst of tool calls used once the calls stop. On a large account a call
/// listing every build, or a long run history, moves hundreds of kilobytes of JSON each way,
/// through buffers that double as they fill, up to 8 MB. A server waiting on its next call allocates nothing, so no collection runs:
/// five calls left one holding 85 MB for as long as its client stayed open, and a machine has a
/// server for every client session. Collected this way, the same server held 17.
/// <para>
/// Most of it is not garbage but buffers returned to <see cref="ArrayPool{T}.Shared"/>, which keeps
/// them in thread local stacks no collection can free. The pool trims only on a full collection,
/// and then only buffers unused for a minute, and the buffers it drops are freed by the collection
/// after. A collection five seconds after the calls freed nothing the pool held. So the quiet
/// period is longer than a minute, and the collection runs twice with the trim between.
/// </para>
/// <para>
/// Aggressive rather than an ordinary full collection, which frees the objects but keeps the
/// memory committed for the next burst: here there may be no next burst for hours.
/// </para>
/// </summary>
sealed class IdleCollector(TimeSpan quiet) : IDisposable
{
    /// <summary>
    /// Longer than the minute the shared pool keeps a buffer it is not handing out.
    /// </summary>
    public static TimeSpan PoolTrimAge { get; } = TimeSpan.FromSeconds(70);

    Timer timer = new(_ => Collect(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    /// <summary>
    /// Wraps a tool call so the quiet period starts again when it ends. Started from the end of the
    /// call rather than its start, since the result is serialised after the handler returns.
    /// </summary>
    public McpRequestHandler<CallToolRequestParams, CallToolResult> After(McpRequestHandler<CallToolRequestParams, CallToolResult> handler) =>
        async (request, cancel) =>
        {
            try
            {
                return await handler(request, cancel);
            }
            finally
            {
                Touch();
            }
        };

    public void Touch() =>
        timer.Change(quiet, Timeout.InfiniteTimeSpan);

    /// <summary>
    /// The first collection runs the pool's trim, as a callback once it ends, and the second frees
    /// what the trim let go of.
    /// </summary>
    static void Collect()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }

    public void Dispose() =>
        timer.Dispose();
}
