/// <summary>
/// What the frame loop rebuilds each time the state changes or the clock ticks over. With a filter
/// typed, the columns are also sized from every row the filter could show.
/// </summary>
[MemoryDiagnoser]
public class ScreenBenchmarks
{
    SessionState state = LargeAccount.State();
    SessionState searched = MonitorSession.Search(LargeAccount.State(), "repository1");

    [Benchmark]
    public object Build() =>
        ScreenBuilder.Build(state, LargeAccount.Now);

    [Benchmark]
    public object BuildSearched() =>
        ScreenBuilder.Build(searched, LargeAccount.Now);
}
