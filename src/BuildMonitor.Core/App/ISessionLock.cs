/// <summary>
/// Whether the desktop session is locked, asked rather than listened for: every desktop has a
/// question to ask off any thread, while the events need the toolkit's own loop, which the native
/// heads do not run on a thread of ours. Null when it cannot tell, which leaves the state as it
/// was rather than guessing either way.
/// </summary>
interface ISessionLock
{
    ValueTask<bool?> IsLocked(Cancel cancel);
}
