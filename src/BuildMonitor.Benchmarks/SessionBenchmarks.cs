/// <summary>
/// Transitions that project the rows, each inside the lock the frame loop takes every frame: a
/// poll's result, and an arrow key.
/// </summary>
[MemoryDiagnoser]
public class SessionBenchmarks
{
    SessionState state = LargeAccount.State();
    ImmutableArray<Build> builds = LargeAccount.Builds();

    [Benchmark]
    public object ApplyPoll() =>
        MonitorSession.ApplyPoll(state, LargeAccount.ConnectionId, [], builds, LargeAccount.Now);

    [Benchmark]
    public object NextRow() =>
        MonitorSession.NextRow(state);
}
