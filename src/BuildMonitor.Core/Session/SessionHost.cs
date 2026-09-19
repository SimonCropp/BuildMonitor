/// <summary>
/// The one mutable reference. Reads are lock free: the render loop takes whatever state is
/// current, and a poll finishing on another thread swaps in the next one under the gate.
/// </summary>
sealed class SessionHost(SessionState initial)
{
    Lock gate = new();
    volatile SessionState state = initial;
    bool mutating;

    public SessionState State => state;

    /// <summary>
    /// <paramref name="change"/> may not mutate again while it runs. The gate is reentrant, so a
    /// nested call does not deadlock: it swaps in its own state, and then the outer call swaps in
    /// the one it had already computed, silently undoing it. An action calling back in here instead
    /// of returning what it wanted is how that gets written, and what it leaves behind is a button
    /// that does nothing, a long way from the line that caused it.
    /// </summary>
    public SessionState Mutate(Func<SessionState, SessionState> change)
    {
        lock (gate)
        {
            if (mutating)
            {
                throw new InvalidOperationException("A mutation cannot mutate. Return the state you want, or take the work off this thread.");
            }

            mutating = true;
            try
            {
                state = change(state);
                return state;
            }
            finally
            {
                mutating = false;
            }
        }
    }
}
