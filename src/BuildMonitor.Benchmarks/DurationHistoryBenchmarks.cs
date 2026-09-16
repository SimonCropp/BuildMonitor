/// <summary>
/// What a poll reads from the duration history whenever the window is open: every pipeline's
/// median.
/// </summary>
[MemoryDiagnoser]
public class DurationHistoryBenchmarks
{
    DurationHistory history = LargeAccount.History();

    [Benchmark]
    public object Medians() =>
        history.Medians();
}
