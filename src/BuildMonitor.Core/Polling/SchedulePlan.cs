/// <summary>
/// One planning pass: every group's plan, the groups to fetch now in order of urgency, the due
/// groups the quota deferred, when to wake next, and the request bucket refilled to now.
/// </summary>
record SchedulePlan(
    ImmutableArray<GroupPlan> Groups,
    ImmutableArray<PollGroup> Fetch,
    ImmutableArray<string> Deferred,
    DateTimeOffset? WakeAt,
    RequestBucket? Bucket);
