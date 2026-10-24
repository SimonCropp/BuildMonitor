static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        TrayApp.Configure();
        try
        {
            return MonitorProgram.Run(args, FormsMonitorWindow.Open, WindowsTray.Open);
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Failed at startup");
            IssueLauncher.LaunchForException("BuildMonitor failed at startup", exception);
            throw;
        }
    }
}
