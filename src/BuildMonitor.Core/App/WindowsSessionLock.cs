/// <summary>
/// Asks Terminal Services for the session's flags, which say whether it is locked. The events for
/// it, SystemEvents.SessionSwitch and WTSRegisterSessionNotification, need a window and its message
/// loop, and the answer is a few microseconds to ask for.
/// </summary>
[SupportedOSPlatform("windows")]
sealed partial class WindowsSessionLock : ISessionLock
{
    const int currentSession = -1;
    const int sessionInfoEx = 25;
    const int stateLock = 0;
    const int stateUnlock = 1;

    // WTSINFOEXW: a DWORD Level, then WTSINFOEX_LEVEL1_W, whose SessionFlags follow its SessionId and
    // SessionState. The LARGE_INTEGERs further in align that to 8, not 4, so at 12 is SessionState,
    // whose WTSActive reads as WTS_SESSIONSTATE_LOCK and had every active session locked.
    const int flagsOffset = 16;

    public ValueTask<bool?> IsLocked(Cancel cancel) =>
        ValueTask.FromResult(Read());

    static bool? Read()
    {
        if (!WTSQuerySessionInformationW(0, currentSession, sessionInfoEx, out var buffer, out var bytes))
        {
            return null;
        }

        try
        {
            if (bytes < flagsOffset + sizeof(int))
            {
                return null;
            }

            var flags = Marshal.ReadInt32(buffer, flagsOffset);
            if (flags == stateLock)
            {
                return true;
            }

            if (flags == stateUnlock)
            {
                return false;
            }

            return null;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSQuerySessionInformationW(nint server, int sessionId, int infoClass, out nint buffer, out int bytes);

    [LibraryImport("wtsapi32.dll")]
    private static partial void WTSFreeMemory(nint memory);
}
