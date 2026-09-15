/// <summary>
/// Why a group is polled as often as it is. The order is the order of urgency when the request
/// quota cannot cover everything that is due.
/// </summary>
enum ScheduleReason
{
    Finishing,
    Running,
    Queued,
    // Running past the slowest its pipeline has taken: still active, but its estimate was wrong.
    Overrun,
    Nudged,
    Unfetched,
    RecentFailure,
    RecentSuccess,
    Quiet,
    Backoff
}
