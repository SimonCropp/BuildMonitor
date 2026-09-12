/// <summary>
/// The one mutable reference. Reads are lock free: the render loop takes whatever state is
/// current, and a poll finishing on another thread swaps in the next one under the gate.
/// </summary>
sealed class SessionHost(SessionState initial)
{
    readonly Lock gate = new();
    volatile SessionState state = initial;

    public SessionState State => state;

    public SessionState Mutate(Func<SessionState, SessionState> change)
    {
        lock (gate)
        {
            state = change(state);
            return state;
        }
    }
}
