/// <summary>
/// Why another branch's newest run has no row, which the hover of its pipeline's row says beside it.
/// </summary>
public enum FoldReason
{
    // It passed, or was cancelled: history rather than something to watch or to read.
    Settled,
    // It failed, and its pull request has been merged since.
    Merged,
    // It failed, and its pull request has been closed since.
    Closed,
    // It failed, and its branch has been deleted since.
    Deleted,
    // It failed, nothing could say whether its branch is still there, and the pipeline's default
    // branch has built since.
    Superseded
}
