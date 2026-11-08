/// <summary>
/// Runs the channel's watch for new failures for the life of the server, which is the life of the
/// session that started it.
/// </summary>
sealed class FailureWatchService(ChannelFeed feed) : BackgroundService
{
    protected override Task ExecuteAsync(Cancel stoppingToken) =>
        feed.WatchFailures(stoppingToken);
}
