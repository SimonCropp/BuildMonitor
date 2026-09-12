static class Logging
{
    /// <summary>
    /// Beside the installed tool, so it moves with the version and the "Open logs" menu item
    /// always finds the logs of the build that is running.
    /// </summary>
    public static string LogsDirectory { get; } = Path.Combine(AssemblyLocation.CurrentDirectory, "logs");

    public static void Init()
    {
        Directory.CreateDirectory(LogsDirectory);
        var configuration = new LoggerConfiguration();
        configuration.MinimumLevel.Debug();
        configuration.WriteTo.File(
            Path.Combine(LogsDirectory, "log.txt"),
            rollOnFileSizeLimit: true,
            fileSizeLimitBytes: 1000000,
            retainedFileCountLimit: 10);
        Log.Logger = configuration.CreateLogger();
    }

    public static void OpenDirectory() =>
        RevealFile.OpenDirectory(LogsDirectory);
}
