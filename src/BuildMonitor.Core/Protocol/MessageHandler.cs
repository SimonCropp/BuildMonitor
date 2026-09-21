/// <summary>
/// Answers the socket. Reads go straight to the state; actions go through the poller, which
/// is what the window's own retry does, so the two cannot disagree.
/// </summary>
sealed class MessageHandler(SessionHost host, Poller poller, Action<string> openUrl, Action<WindowCommand> window, ArtifactStore artifacts, Func<DateTimeOffset>? clock = null)
{
    public async Task<Response> Handle(Message message)
    {
        var state = host.State;
        var now = clock?.Invoke() ?? DateTimeOffset.UtcNow;
        var context = DtoContext.Default;
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
                return Response.Success(JsonSerializer.Serialize(Snapshot.Builds(state, now), context.ListBuildDto));
            case Verb.Get:
            {
                if (message.Key is null ||
                    Snapshot.Find(state, message.Key, now) is not { } build)
                {
                    return Response.Error($"No build with key {message.Key}");
                }

                return Response.Success(JsonSerializer.Serialize(build, context.BuildDto));
            }
            case Verb.Runs:
            {
                if (message.Key is null ||
                    Snapshot.Runs(state, message.Key, now) is not { } runs)
                {
                    return Response.Error($"No pipeline with key {message.Key}");
                }

                return Response.Success(JsonSerializer.Serialize(runs, context.ListBuildDto));
            }
            case Verb.Pipelines:
                return Response.Success(JsonSerializer.Serialize(Snapshot.Pipelines(state), context.ListPipelineDto));
            case Verb.Refresh:
                // Refused rather than answered as though a poll had started: a mistyped id would
                // otherwise leave whoever asked waiting on a refresh that never happens.
                if (!string.IsNullOrEmpty(message.Key) &&
                    state.Connections.All(_ => _.Connection.Id != message.Key))
                {
                    return Response.Error($"No connection with id {message.Key}");
                }

                poller.Refresh(string.IsNullOrEmpty(message.Key) ? null : message.Key);
                return Response.Success();
            case Verb.Retry:
            case Verb.Cancel:
            case Verb.RunNext:
            {
                var build = state.Builds.FirstOrDefault(_ => _.HasKey(message.Key));
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

                if (message.Verb == Verb.RunNext)
                {
                    // Through the same check the chip is drawn from, so an assistant cannot ask for
                    // something a click could not: the service has the call, and the run is still
                    // queued rather than already going.
                    if (MonitorSession.Descriptor(state, build) is not { } descriptor ||
                        !build.CanRunNext(descriptor))
                    {
                        return Response.Error("That build cannot be moved to the front of the queue");
                    }

                    await poller.RunNext(build, Cancel.None);
                    host.Mutate(_ => MonitorSession.SetStatus(_, $"Moved {build.PipelineName} to the front of the queue"));
                    return Response.Success("Moved to the front of the queue");
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
                return Response.Success(JsonSerializer.Serialize(Snapshot.Connections(state), context.ListConnectionDto));
            case Verb.Summary:
                return Response.Success(JsonSerializer.Serialize(Snapshot.Summary(state, now), context.SummaryDto));
            case Verb.Open:
            {
                var build = state.Builds.FirstOrDefault(_ => _.HasKey(message.Key));
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
                var build = state.Builds.FirstOrDefault(_ => _.HasKey(message.Key));
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
            case Verb.Triage:
            {
                var build = state.Builds.FirstOrDefault(_ => _.HasKey(message.Key));
                if (build is null)
                {
                    return Response.Error($"No build with key {message.Key}");
                }

                if (!build.LogCopyable())
                {
                    return Response.Error("That build did not fail, so there is nothing to collect");
                }

                // Unlike the chip, this does not ask for a local checkout. The chip's promise is to
                // triage the code the user has here; a tool may be asked for a test report on a
                // build nobody has cloned, and the prompt already handles a build with no directory.
                artifacts.Sweep();
                var files = await ArtifactCollector.Collect(artifacts, poller, build, Cancel.None);
                return Response.Success(JsonSerializer.Serialize(files, context.TriageFilesDto));
            }
            default:
                return Response.Error($"Unknown verb {message.Verb}");
        }
    }

    /// <summary>
    /// How much of the log was asked for. A body that is missing or not a size, as one from a
    /// launcher too old to send one is, takes the default rather than the whole log.
    /// </summary>
    static int Lines(string? body)
    {
        if (int.TryParse(body, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lines) &&
            lines > 0)
        {
            return lines;
        }

        return LogTail.DefaultLines;
    }
}
