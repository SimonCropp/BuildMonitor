/// <summary>
/// Transitions that project the rows, each inside the lock the frame loop takes every frame: a
/// poll's result, and an arrow key.
/// </summary>
[MemoryDiagnoser]
public class SessionBenchmarks
{
    SessionState state = LargeAccount.State();
    SessionState hidden = MonitorSession.Hide(LargeAccount.State());
    ImmutableArray<Pipeline> pipelines = LargeAccount.Pipelines();
    ImmutableArray<Build> builds = LargeAccount.Builds();

    [Benchmark]
    public object ApplyPoll() =>
        MonitorSession.ApplyPoll(state, LargeAccount.ConnectionId, pipelines, builds, LargeAccount.Now);

    [Benchmark]
    public object NextRow() =>
        MonitorSession.NextRow(state);

    /// <summary>
    /// What the frame loop does around a poll: draw the state, apply the poll to it, draw the result.
    /// The drawn state is a copy each time, as each poll starts from a state the last one left.
    /// </summary>
    [Benchmark]
    public object PollCycle() =>
        Cycle(state);

    /// <summary>
    /// The same while the window is hidden, when only the tray reads the screen.
    /// </summary>
    [Benchmark]
    public object HiddenPollCycle() =>
        Cycle(hidden);

    object Cycle(SessionState start)
    {
        var drawn = start with { Builds = [..start.Builds] };
        ScreenBuilder.Build(drawn, LargeAccount.Now);
        var polled = MonitorSession.ApplyPoll(drawn, LargeAccount.ConnectionId, pipelines, builds, LargeAccount.Now);
        return ScreenBuilder.Build(polled, LargeAccount.Now);
    }
}
