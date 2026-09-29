/// <summary>
/// What a poll reads from the duration history whenever the window is open: every pipeline's
/// median. The history hands back the medians it last made until a run is recorded, so the second
/// records one first, as a poll that saw a build finish does.
/// </summary>
[MemoryDiagnoser]
public class DurationHistoryBenchmarks
{
    DurationHistory history = LargeAccount.History();
    string pipelineKey = LargeAccount.Builds()[0].PipelineKey;
    int runs;

    [Benchmark]
    public object Medians() =>
        history.Medians();

    [Benchmark]
    public object MediansAfterARun()
    {
        history.Record(pipelineKey, TimeSpan.FromSeconds(200 + ++runs % 60));
        return history.Medians();
    }
}
