static class SessionLocks
{
    /// <summary>
    /// The Windows and macOS answers. Linux asks logind over the system bus, which only the native
    /// head has a client for, so it passes its own to <see cref="MonitorProgram.Run"/>.
    /// </summary>
    public static ISessionLock? ForPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsSessionLock();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacSessionLock();
        }

        return null;
    }
}
