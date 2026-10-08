/// <summary>
/// The body of a workflow rerun: only the jobs that failed and those after them, or every job.
/// </summary>
record CircleCiRerun(bool FromFailed);
