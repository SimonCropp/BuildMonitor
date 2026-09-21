/// <summary>
/// Every provider's vocabulary folded into one. Keep in sync with BmStatus in bm.h.
/// </summary>
public enum BuildStatus
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
    Unknown = 5
}
