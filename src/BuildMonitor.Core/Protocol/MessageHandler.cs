/// <summary>
/// Answers the socket. Reads go straight to the state; actions go through the poller, which
/// is what the window's own retry does, so the two cannot disagree.
/// </summary>
sealed class MessageHandler(SessionHost host, Poller poller, Action<string> openUrl, Action<WindowCommand> window, Func<DateTimeOffset>? clock = null)
{
    public async Task<Response> Handle(Message message)
    {
        var state = host.State;
        var now = clock?.Invoke() ?? DateTimeOffset.UtcNow;
        switch (message.Verb)
        {
            case Verb.Ping:
                return Response.Success(VersionReader.VersionString);
            case Verb.Show:
                host.Mutate(MonitorSession.Show);
                window(WindowCommand.Show);
                return Response.Success();
            case Verb.Hide:
                host.Mutate(MonitorSession.Hide);
                window(WindowCommand.Hide);
                return Response.Success();
            case Verb.Quit:
                host.Mutate(MonitorSession.Quit);
                return Response.Success();
            case Verb.List:
                return Response.Success(JsonSerializer.Serialize(Snapshot.Builds(state, now), DtoContext.Default.ListBuildDto));
            case Verb.Get:
            {
                if (message.Key is null ||
                    Snapshot.Find(state, message.Key, now) is not { } build)
                {
                    return Response.Error($"No build with key {message.Key}");
                }

                return Response.Success(JsonSerializer.Serialize(build, DtoContext.Default.BuildDto));
            }
            case Verb.Runs:
            {
                if (message.Key is null ||
                    Snapshot.Runs(state, message.Key, now) is not { } runs)
                {
                    return Response.Error($"No pipeline with key {message.Key}");
                }

                return Response.Success(JsonSerializer.Serialize(runs, DtoContext.Default.ListBuildDto));
            }
            case Verb.Pipelines:
                return Response.Success(JsonSerializer.Serialize(Snapshot.Pipelines(state), DtoContext.Default.ListPipelineDto));
            case Verb.Refresh:
                poller.Refresh(string.IsNullOrEmpty(message.Key) ? null : message.Key);
                return Response.Success();
            case Verb.Retry:
            case Verb.Cancel:
            {
                var build = state.Builds.FirstOrDefault(_ => _.Key == message.Key);
                if (build is null)
                {
                    return Response.Error($"No build with key {message.Key}");
                }

                if (message.Verb == Verb.Retry)
                {
                    if (!build.Retryable())
                    {
                        return Response.Error("That build cannot be retried");
                    }

                    await poller.Retry(build, Cancel.None);
                    host.Mutate(_ => MonitorSession.SetStatus(_, $"Retried {build.PipelineName} {build.RunNumberLabel()}".TrimEnd()));
                    return Response.Success("Retried");
                }

                if (!build.CanCancel)
                {
                    return Response.Error("That build cannot be cancelled");
                }

                await poller.Cancel(build, Cancel.None);
                host.Mutate(_ => MonitorSession.SetStatus(_, $"Cancelled {build.PipelineName} {build.RunNumberLabel()}".TrimEnd()));
                return Response.Success("Cancelled");
            }
            case Verb.Connections:
                return Response.Success(JsonSerializer.Serialize(Snapshot.Connections(state), DtoContext.Default.ListConnectionDto));
            case Verb.Summary:
                return Response.Success(JsonSerializer.Serialize(Snapshot.Summary(state, now), DtoContext.Default.SummaryDto));
            case Verb.Open:
            {
                var build = state.Builds.FirstOrDefault(_ => _.Key == message.Key);
                if (build is null)
                {
                    return Response.Error($"No build with key {message.Key}");
                }

                var url = message.Body switch
                {
                    "branch" => build.BranchUrl,
                    "pr" => build.PullRequestUrl,
                    _ => build.BuildUrl
                };
                if (url is null)
                {
                    return Response.Error($"That build has no {message.Body} link");
                }

                openUrl(url);
                return Response.Success(url);
            }
            case Verb.Log:
            {
                var build = state.Builds.FirstOrDefault(_ => _.Key == message.Key);
                if (build is null)
                {
                    return Response.Error($"No build with key {message.Key}");
                }

                var log = await poller.FetchLog(build, Cancel.None);
                if (log.Length == 0)
                {
                    return Response.Error("That build has no log");
                }

                return Response.Success(LogTail.Take(log, Lines(message.Body)));
            }
            default:
                return Response.Error($"Unknown verb {message.Verb}");
        }
    }

    /// <summary>
    /// How much of the log was asked for. A body that is missing or not a size, as one from a
    /// launcher too old to send one is, takes the default rather than the whole log.
    /// </summary>
    static int Lines(string? body) =>
        int.TryParse(body, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lines) && lines > 0
            ? lines
            : LogTail.DefaultLines;
}
