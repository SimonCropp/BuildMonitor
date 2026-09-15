using Serilog.Events;

public class LoggingTests
{
    [Test]
    [Arguments("Debug", LogEventLevel.Debug)]
    [Arguments("verbose", LogEventLevel.Verbose)]
    [Arguments("Warning", LogEventLevel.Warning)]
    public async Task TheVariableNamesTheLevel(string variable, LogEventLevel level) =>
        await Assert.That(Logging.MinimumLevel(variable)).IsEqualTo(level);

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("loud")]
    [Arguments("42")]
    public async Task OtherwiseOnlyADebugBuildLogsDebug(string? variable)
    {
#if DEBUG
        var level = LogEventLevel.Debug;
#else
        var level = LogEventLevel.Information;
#endif
        await Assert.That(Logging.MinimumLevel(variable)).IsEqualTo(level);
    }
}
