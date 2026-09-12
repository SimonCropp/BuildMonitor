/// <summary>
/// The canonical states every screen and applier test starts from. One clock, one window size,
/// three connections that between them cover a running build with a history estimate, a provider
/// estimate, a failure with a pull request, a queued build and a deployment.
/// </summary>
static class Fixtures
{
    public static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public static readonly Connection GitHub = new()
    {
        Id = "gh",
        ProviderId = "github",
        Name = "GitHub",
        Auth = AuthMethod.Token
    };

    public static readonly Connection Jenkins = new()
    {
        Id = "jenkins",
        ProviderId = "jenkins",
        Name = "Jenkins",
        Server = "https://jenkins.example.com",
        User = "simon",
        Auth = AuthMethod.Token
    };

    public static readonly Connection Octopus = new()
    {
        Id = "octo",
        ProviderId = "octopus",
        Name = "Octopus",
        Server = "https://octopus.example.com",
        Auth = AuthMethod.Token
    };

    public static Settings Settings() =>
        new()
        {
            Connections = [GitHub, Jenkins, Octopus]
        };

    public static SessionState Empty() =>
        MonitorSession.Resize(SessionState.Start(new()), 120, 30);

    public static SessionState Connected() =>
        MonitorSession.Resize(SessionState.Start(Settings()), 120, 30);

    public static SessionState WithBuilds()
    {
        var state = Connected();
        state = MonitorSession.ApplyPoll(state, GitHub.Id, [], GitHubBuilds(), Now - TimeSpan.FromSeconds(12));
        state = MonitorSession.ApplyPoll(state, Jenkins.Id, [], JenkinsBuilds(), Now - TimeSpan.FromSeconds(40));
        state = MonitorSession.ApplyPoll(state, Octopus.Id, [], OctopusBuilds(), Now - TimeSpan.FromSeconds(5));
        return MonitorSession.ApplyMedians(
            state,
            ImmutableDictionary<string, TimeSpan>.Empty
                .Add("gh/DiffEngine/test.yml", TimeSpan.FromMinutes(6)));
    }

    public static ImmutableArray<Build> GitHubBuilds() =>
    [
        Build(
            GitHub.Id,
            "DiffEngine/test.yml",
            "test.yml",
            "VerifyTests/DiffEngine",
            "main",
            "1234",
            BuildStatus.Running,
            started: Now - TimeSpan.FromMinutes(3),
            branchUrl: "https://github.com/VerifyTests/DiffEngine/tree/main",
            commitMessage: "Fix the thing",
            author: "SimonCropp",
            sha: "0123456789abcdef"),
        Build(
            GitHub.Id,
            "Verify/test.yml",
            "test.yml",
            "VerifyTests/Verify",
            "feature/inline",
            "77",
            BuildStatus.Failed,
            started: Now - TimeSpan.FromMinutes(30),
            finished: Now - TimeSpan.FromMinutes(25),
            branchUrl: "https://github.com/VerifyTests/Verify/tree/feature/inline",
            pullRequest: "42",
            pullRequestUrl: "https://github.com/VerifyTests/Verify/pull/42",
            commitMessage: "Inline snapshots\n\nlonger description",
            author: "SimonCropp"),
        Build(
            GitHub.Id,
            "Verify/test.yml",
            "test.yml",
            "VerifyTests/Verify",
            "main",
            "76",
            BuildStatus.Succeeded,
            started: Now - TimeSpan.FromHours(2),
            finished: Now - TimeSpan.FromHours(2) + TimeSpan.FromMinutes(5),
            branchUrl: "https://github.com/VerifyTests/Verify/tree/main"),
        Build(
            GitHub.Id,
            "DiffEngine/docs.yml",
            "docs.yml",
            "VerifyTests/DiffEngine",
            "main",
            "300",
            BuildStatus.Succeeded,
            started: Now - TimeSpan.FromDays(1),
            finished: Now - TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1),
            branchUrl: "https://github.com/VerifyTests/DiffEngine/tree/main")
    ];

    public static ImmutableArray<Build> JenkinsBuilds() =>
    [
        Build(
            Jenkins.Id,
            "build-all",
            "Build all",
            "build-all",
            "main",
            "501",
            BuildStatus.Running,
            started: Now - TimeSpan.FromMinutes(1),
            estimate: new(TimeSpan.FromMinutes(5), null, null),
            canCancel: true,
            canRetry: false),
        Build(
            Jenkins.Id,
            "nightly",
            "Nightly",
            "nightly",
            null,
            "88",
            BuildStatus.Queued,
            queued: Now - TimeSpan.FromSeconds(30),
            canCancel: true,
            canRetry: false)
    ];

    public static ImmutableArray<Build> OctopusBuilds() =>
    [
        Build(
            Octopus.Id,
            "Projects-1",
            "Deploy Web",
            "Deploy Web",
            null,
            "12",
            BuildStatus.Running,
            started: Now - TimeSpan.FromSeconds(90),
            estimate: new(null, 40, TimeSpan.FromSeconds(135)),
            canRetry: false,
            canCancel: true)
    ];

    public static Build Build(
        string connectionId,
        string pipelineId,
        string pipelineName,
        string repo,
        string? branch,
        string run,
        BuildStatus status,
        DateTimeOffset? queued = null,
        DateTimeOffset? started = null,
        DateTimeOffset? finished = null,
        ProviderEstimate? estimate = null,
        string? branchUrl = null,
        string? pullRequest = null,
        string? pullRequestUrl = null,
        string? commitMessage = null,
        string? author = null,
        string? sha = null,
        bool canRetry = true,
        bool canCancel = true) =>
        new(
            connectionId,
            pipelineId,
            pipelineName,
            repo,
            branch,
            run,
            status,
            null,
            queued ?? started,
            started,
            finished,
            estimate,
            $"https://example.com/{connectionId}/{pipelineId}/{run}",
            branchUrl,
            pullRequest,
            pullRequestUrl,
            sha,
            commitMessage,
            author,
            status is BuildStatus.Queued or BuildStatus.Running ? canRetry && false : canRetry,
            status is BuildStatus.Queued or BuildStatus.Running && canCancel,
            run);

    public static SessionState WithMenu() =>
        MonitorSession.OpenMenu(WithBuilds(), 2);

    public static SessionState Folded() =>
        MonitorSession.ToggleGroup(WithBuilds(), GitHub.Id);

    public static SessionState Scrolled() =>
        MonitorSession.SelectRow(MonitorSession.Resize(WithBuilds(), 120, 12), 8);

    public static SessionState Narrow() =>
        MonitorSession.Resize(WithBuilds(), 80, 30);

    public static SessionState Options() =>
        MonitorSession.OpenOptions(WithBuilds());

    public static SessionState Filters()
    {
        var state = WithBuilds();
        state = MonitorSession.ApplySettings(
            state,
            state.Settings with
            {
                Filters = [new(FilterKind.Prefix, FilterTarget.Pipeline, "docs")]
            });
        return MonitorSession.OpenFilters(state);
    }

    public static SessionState ConnectionNew() =>
        MonitorSession.OpenConnectionEditor(WithBuilds(), null, "draft1");

    public static SessionState ConnectionEdit() =>
        MonitorSession.OpenConnectionEditor(WithBuilds(), Jenkins.Id, "unused");

    public static SessionState SignInDevice()
    {
        var state = MonitorSession.FieldChanged(ConnectionNew(), FormFields.Provider, "GitHub Actions");
        state = MonitorSession.FieldChanged(state, FormFields.Auth, nameof(AuthMethod.Device));
        var flow = Guid.Parse("00000000-0000-0000-0000-000000000001");
        state = MonitorSession.BeginSignIn(state, ConnectionDraft.Build(state.Form!), AuthMethod.Device, flow);
        return MonitorSession.SignInProgress(state, flow, "ABCD-1234", "https://github.com/login/device");
    }

    public static SessionState Polling() =>
        MonitorSession.SetProgress(
            MonitorSession.SetHealth(WithBuilds(), GitHub.Id, ConnectionHealth.Polling),
            GitHub.Id,
            new(15, 20));

    public static SessionState NeedsAuth() =>
        MonitorSession.SetHealth(WithBuilds(), GitHub.Id, ConnectionHealth.NeedsAuth, "401 Unauthorized");

    public static SessionState RateLimited() =>
        MonitorSession.SetHealth(WithBuilds(), GitHub.Id, ConnectionHealth.RateLimited, "429", Now + TimeSpan.FromMinutes(4));

    public static SessionState Hidden() =>
        MonitorSession.Hide(WithBuilds());

    public static string Render(SessionState state) =>
        AsciiRenderer.Render(ScreenBuilder.Build(state, Now));
}
