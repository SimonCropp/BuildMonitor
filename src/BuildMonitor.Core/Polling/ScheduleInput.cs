/// <summary>
/// Everything <see cref="PollSchedule.Plan"/> needs, so planning is a pure function of values the
/// poller already holds: its groups and what it remembers of each, the connection's builds from
/// state, the pipelines' run durations from the history, the rate state, the request bucket, and
/// the time.
/// </summary>
/// <param name="Builds">The connection's builds, after the current filters.</param>
/// <param name="Durations">Pipeline key to the fastest and slowest of its recent successful runs.</param>
/// <param name="Everything">A refresh: every group is due now.</param>
record ScheduleInput(
    string ConnectionId,
    RequestQuota? Quota,
    TimeSpan? IdleCap,
    ImmutableArray<PollGroup> Groups,
    ImmutableDictionary<string, GroupMemory> Memory,
    ImmutableArray<Build> Builds,
    ImmutableDictionary<string, DurationRange> Durations,
    TimeSpan Interval,
    TimeSpan RunningInterval,
    RateState Rate,
    DateTimeOffset? PausedUntil,
    RequestBucket? Bucket,
    bool Everything,
    DateTimeOffset Now);
