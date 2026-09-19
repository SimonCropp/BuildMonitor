public class StatusPrinterTests
{
    [Test]
    public Task Render()
    {
        var state = Fixtures.WithBuilds();
        return Verify(StatusPrinter.Render(Snapshot.Summary(state, Fixtures.Now), Snapshot.Builds(state, Fixtures.Now)).TrimEnd())
            .Snapshot(
                """
                6 pipelines, 1 failing, 4 running. Polled 5s ago
                  GitHub: GitHub Actions, Ok
                  Jenkins: Jenkins, Ok
                  Octopus: Octopus Deploy, Ok

                  build-all   Build all   main            #501     Running    04:00 left   https://example.com/jenkins/build-all/501
                  Deploy Web  Deploy Web                  #12      Running    02:15 left   https://example.com/octo/Projects-1/12
                  DiffEngine  test.yml    main            #1234    Running    03:00 left   https://example.com/gh/DiffEngine/test.yml/1234
                  nightly     Nightly                     #88      Queued     queued 30s   https://example.com/jenkins/nightly/88
                  Verify      test.yml    feature/inline  #77      Failed     25m ago      https://example.com/gh/Verify/test.yml/77
                  DiffEngine  docs.yml    main            #300     Succeeded  23h ago      https://example.com/gh/DiffEngine/docs.yml/300
                """);
    }

    [Test]
    public Task DependabotBranchesLeaveOutTheEcosystem()
    {
        var state = Fixtures.WithDependabotFailure();
        return Verify(StatusPrinter.Render(Snapshot.Summary(state, Fixtures.Now), Snapshot.Builds(state, Fixtures.Now)).TrimEnd())
            .Snapshot(
                """
                7 pipelines, 2 failing, 4 running. Polled 5s ago
                  GitHub: GitHub Actions, Ok
                  Jenkins: Jenkins, Ok
                  Octopus: Octopus Deploy, Ok

                  build-all   Build all   main               #501     Running    04:00 left   https://example.com/jenkins/build-all/501
                  Deploy Web  Deploy Web                     #12      Running    02:15 left   https://example.com/octo/Projects-1/12
                  DiffEngine  test.yml    main               #1234    Running    03:00 left   https://example.com/gh/DiffEngine/test.yml/1234
                  nightly     Nightly                        #88      Queued     queued 30s   https://example.com/jenkins/nightly/88
                  Reports     build.yml   🤖 Polyfill-9.1.0  #9       Failed     8m ago       https://example.com/gh/Reports/build.yml/9
                  Verify      test.yml    feature/inline     #77      Failed     25m ago      https://example.com/gh/Verify/test.yml/77
                  DiffEngine  docs.yml    main               #300     Succeeded  23h ago      https://example.com/gh/DiffEngine/docs.yml/300
                """);
    }
}
