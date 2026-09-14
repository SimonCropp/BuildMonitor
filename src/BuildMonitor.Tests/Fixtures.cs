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

    public static readonly GroupKey VerifyPassing = new("Verify", false);

    public static readonly GroupKey VerifyFailing = new("Verify", true);

    public static int RowOf(SessionState state, Func<Row, bool> match)
    {
        var rows = RowProjection.Rows(state);
        for (var index = 0; index < rows.Length; index++)
        {
            if (match(rows[index]))
            {
                return index;
            }
        }

        throw new("No row matches");
    }

    /// <summary>
    /// Verify gains two green workflows beside its failing one, so the green pair shares a closed
    /// group while DiffEngine's single green workflow keeps its own row.
    /// </summary>
    public static SessionState WithGreenProject() =>
        MonitorSession.ApplyPoll(
            WithBuilds(),
            GitHub.Id,
            [],
            [
                ..GitHubBuilds(),
                Build(
                    GitHub.Id,
                    "Verify/docs.yml",
                    "docs.yml",
                    "VerifyTests/Verify",
                    "main",
                    "40",
                    BuildStatus.Succeeded,
                    started: Now - TimeSpan.FromHours(3),
                    finished: Now - TimeSpan.FromHours(3) + TimeSpan.FromMinutes(2),
                    branchUrl: "https://github.com/VerifyTests/Verify/tree/main"),
                Build(
                    GitHub.Id,
                    "Verify/nuget.yml",
                    "nuget.yml",
                    "VerifyTests/Verify",
                    "main",
                    "12",
                    BuildStatus.Succeeded,
                    started: Now - TimeSpan.FromHours(5),
                    finished: Now - TimeSpan.FromHours(5) + TimeSpan.FromMinutes(4),
                    branchUrl: "https://github.com/VerifyTests/Verify/tree/main")
            ],
            Now - TimeSpan.FromSeconds(12));

    /// <summary>
    /// <see cref="WithGreenProject"/> plus a failing release workflow, so Verify has an open red
    /// group of two beside its closed green one.
    /// </summary>
    public static SessionState WithFailedGroup()
    {
        var state = WithGreenProject();
        return MonitorSession.ApplyPoll(
            state,
            GitHub.Id,
            [],
            [
                ..state.Builds.Where(_ => _.ConnectionId == GitHub.Id),
                Build(
                    GitHub.Id,
                    "Verify/release.yml",
                    "release.yml",
                    "VerifyTests/Verify",
                    "main",
                    "9",
                    BuildStatus.Failed,
                    started: Now - TimeSpan.FromHours(1),
                    finished: Now - TimeSpan.FromMinutes(50),
                    branchUrl: "https://github.com/VerifyTests/Verify/tree/main")
            ],
            Now - TimeSpan.FromSeconds(12));
    }

    /// <summary>
    /// Only GitHub, so no row needs a provider icon to tell it apart.
    /// </summary>
    public static SessionState SingleProvider()
    {
        var state = MonitorSession.Resize(SessionState.Start(new() { Connections = [GitHub] }), 120, 30);
        return MonitorSession.ApplyPoll(state, GitHub.Id, [], GitHubBuilds(), Now - TimeSpan.FromSeconds(12));
    }

    public static SessionState ConnectionErrors() =>
        MonitorSession.SetHealth(RateLimited(), Jenkins.Id, ConnectionHealth.Error, "500 Internal Server Error");

    public static SessionState WithMenu() =>
        MonitorSession.OpenMenu(WithBuilds(), 2);

    /// <summary>
    /// Nine rows in a body of six, with the last selected, so the first three scroll away.
    /// </summary>
    public static SessionState Scrolled() =>
        MonitorSession.SelectRow(MonitorSession.Resize(WithFailedGroup(), 120, 12), 8);

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
