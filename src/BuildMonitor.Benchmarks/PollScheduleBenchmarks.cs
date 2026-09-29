/// <summary>
/// What a connection's loop does twice a cycle, before it fetches and again to know when to wake:
/// read every pipeline's fastest and slowest run from the history, and plan when each repository
/// is next due from every build it holds. The repositories were all fetched a minute ago, as in a
/// session that has run a while, so each one's interval is worked out from its builds.
/// </summary>
[MemoryDiagnoser]
public class PollScheduleBenchmarks
{
    ScheduleInput input = Input();
    DurationHistory history = LargeAccount.History();

    [Benchmark]
    public object Plan() =>
        PollSchedule.Plan(input);

    [Benchmark]
    public object Ranges() =>
        history.Ranges();

    static ScheduleInput Input()
    {
        var groups = PollGroup.Of(FetchUnit.Repository, LargeAccount.Pipelines());
        var fetched = LargeAccount.Now - TimeSpan.FromMinutes(1);
        var memory = groups.ToImmutableDictionary(
            _ => _.Key,
            _ => GroupMemory.New with
            {
                LastAttempt = fetched,
                FetchedPipelines = [.._.Pipelines.Select(_ => _.Id)]
            });
        return new(
            LargeAccount.ConnectionId,
            ProviderDescriptors.GitHub.Quota,
            ProviderDescriptors.GitHub.IdleCap,
            groups,
            memory,
            LargeAccount.Builds(),
            LargeAccount.History().Ranges(),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(10),
            RateState.Unknown,
            null,
            null,
            false,
            LargeAccount.Now);
    }
}
