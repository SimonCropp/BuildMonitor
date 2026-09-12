static class Program
{
    static int Main(string[] args)
    {
        NativeResolver.Register();
        try
        {
            return MonitorProgram.Run(args, NativeMonitorWindow.Open, OperatingSystem.IsMacOS() ? NativeTray.Open : SniTray.Open);
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Failed at startup");
            IssueLauncher.LaunchForException("BuildMonitor failed at startup", exception);
            throw;
        }
    }
}
