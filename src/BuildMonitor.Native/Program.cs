static class Program
{
    static int Main(string[] args)
    {
        Bm.Resolve();
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                return MonitorProgram.Run(args, NativeMonitorWindow.Open, NativeTray.Open);
            }

            // Not in Core with the others: only this head has a D-Bus client.
            ISessionLock? sessionLock = OperatingSystem.IsLinux() ? new LoginSessionLock() : null;
            return MonitorProgram.Run(args, NativeMonitorWindow.Open, LinuxTray.Open, sessionLock);
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Failed at startup");
            IssueLauncher.LaunchForException("BuildMonitor failed at startup", exception);
            throw;
        }
    }
}
