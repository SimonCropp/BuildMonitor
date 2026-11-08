using ModelContextProtocol.Server;

/// <summary>
/// The tools a server run as a Claude Code channel adds. Thin in the way <see cref="BuildTools"/>
/// is: the watch is <see cref="ChannelFeed"/>'s.
/// <para>
/// Only on a channel, since a watch's answer is an event, and anywhere else an assistant would be
/// told it was watching and then never hear.
/// </para>
/// </summary>
[McpServerToolType]
sealed class ChannelTools(ChannelFeed feed)
{
    [McpServerTool(Name = "watch_build", ReadOnly = true)]
    [Description("Watches a build in the background and returns at once. When it finishes, an event arrives in this session as a channel tag, even while the session is idle: use this rather than wait_for_build when the user wants to be told later and to carry on meanwhile. Takes the choices wait_for_build does. Gives up after 12 hours, with an event saying so.")]
    public async Task<string> WatchBuild(
        [Description("The build key from list_builds.")]
        string key,
        [Description("As wait_for_build's: when the run the key shows has already finished, watch the one after it, the run a push or retry_build just set off. The tray is asked to poll that pipeline now. Defaults to false.")]
        bool nextRun = false,
        [Description("As wait_for_build's: watch every run of the push this run was part of, and say in one event how they all ended. Fails for a build whose service reports no commit. Defaults to false.")]
        bool wholePush = false,
        [Description("With wholePush, send the event at the first failure, with the rest as they stood, rather than once every run has finished. A cancelled run is not a failure. Defaults to false.")]
        bool stopOnFailure = false,
        Cancel cancel = default)
    {
        var scope = BuildTools.Scope(wholePush, stopOnFailure);
        var build = await feed.Watch(key, nextRun, scope, cancel);
        return ChannelText.Watching(build, nextRun, scope);
    }
}
