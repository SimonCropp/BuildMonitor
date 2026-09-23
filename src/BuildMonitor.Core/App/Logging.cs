static class Logging
{
    /// <summary>
    /// A level, such as Debug, to log at instead of the build's own.
    /// </summary>
    public const string Variable = "BuildMonitor_LogLevel";

    static string currentDirectory { get; } = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    /// <summary>
    /// Beside the installed tool, so it moves with the version and the "Open logs" menu item
    /// always finds the logs of the build that is running.
    /// </summary>
    public static string LogsDirectory { get; } = Path.Combine(currentDirectory, "logs");

    public static void Init()
    {
        Directory.CreateDirectory(LogsDirectory);
        var configuration = new LoggerConfiguration();
        configuration.MinimumLevel.Is(MinimumLevel(Environment.GetEnvironmentVariable(Variable)));
        configuration.WriteTo.File(
            Path.Combine(LogsDirectory, "log.txt"),
            rollOnFileSizeLimit: true,
            fileSizeLimitBytes: 1000000,
            retainedFileCountLimit: 10);
        Log.Logger = configuration.CreateLogger();
    }

    /// <summary>
    /// Information, unless a Debug build or <paramref name="variable"/> asks for another level.
    /// Debug in every build filled the log with each connection's schedule: two connections of 167
    /// and 100 groups wrote 953 KB of a 1 MB file in under seven minutes, so the ten files kept held
    /// about an hour, and "Open logs" and "Raise issue" missed anything older.
    /// </summary>
    public static LogEventLevel MinimumLevel(string? variable)
    {
        if (Enum.TryParse<LogEventLevel>(variable, true, out var level) &&
            Enum.IsDefined(level))
        {
            return level;
        }

#if DEBUG
        return LogEventLevel.Debug;
#else
        return LogEventLevel.Information;
#endif
    }

    /// <summary>
    /// Created first: nothing has been logged yet on a run that has only ever succeeded, and an
    /// Open logs that opened nothing would read as broken.
    /// </summary>
    public static void OpenDirectory()
    {
        Directory.CreateDirectory(LogsDirectory);
        RevealFile.OpenDirectory(LogsDirectory);
    }
}
