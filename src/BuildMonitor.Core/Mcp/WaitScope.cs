/// <summary>
/// What a wait is for once it has its run. One value rather than a flag for the push and another
/// for stopping, since stopping at a failure means nothing for a single run, which a failure ends
/// anyway, and two flags read at the call as a row of bools whose order said which was which.
/// </summary>
enum WaitScope
{
    // The run alone.
    Run,

    // Every run of the push the run was part of, until none is queued or running.
    Push,

    // Every run of the push, until none is queued or running or one has failed: a lint failure
    // two minutes in is the answer, and the half hour of tests after it would only delay it.
    PushUntilFailure
}
