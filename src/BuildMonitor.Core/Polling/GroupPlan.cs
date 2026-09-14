/// <summary>
/// When one group is next due, and why at that interval.
/// </summary>
record GroupPlan(string Key, ScheduleReason Reason, TimeSpan Interval, DateTimeOffset DueAt);
