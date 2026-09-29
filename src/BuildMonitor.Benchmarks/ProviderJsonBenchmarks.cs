/// <summary>
/// A provider's answer parsed and read the way a poll reads it. A Jenkins job whose builds each
/// carry the 25 parameters the tree asks for, of every type a job declares, asked who each build
/// is for. An Azure DevOps page of pull request builds, each with its property collection and the
/// variables it was queued with, asked who each is for and which branches it came from and targets.
/// </summary>
[MemoryDiagnoser]
public class ProviderJsonBenchmarks
{
    byte[] jenkins = JenkinsJob(50);
    byte[] azure = AzureDevOpsBuilds(200);

    [Benchmark]
    public int Jenkins()
    {
        var job = JsonSerializer.Deserialize(jenkins, JenkinsContext.Default.JenkinsJob)!;
        var named = 0;
        foreach (var build in job.Builds)
        {
            if (build.Parameter(TriggeredBy.Property) is not null)
            {
                named++;
            }
        }

        return named;
    }

    [Benchmark]
    public int AzureDevOps()
    {
        var page = JsonSerializer.Deserialize(azure, AzureDevOpsContext.Default.AzureDevOpsListAzureDevOpsBuild)!;
        var found = 0;
        foreach (var build in page.Value)
        {
            found += Count(build.Property(TriggeredBy.Property));
            found += Count(build.Parameter("system.pullRequest.targetBranch"));
            found += Count(build.Parameter("system.pullRequest.isFork"));
            found += Count(build.Parameter("system.pullRequest.sourceBranch"));
        }

        return found;
    }

    static int Count(string? value)
    {
        if (value is null)
        {
            return 0;
        }

        return 1;
    }

    static byte[] JenkinsJob(int count)
    {
        var builder = new StringBuilder("""{"_class":"hudson.model.FreeStyleProject","builds":[""");
        for (var number = count; number > 0; number--)
        {
            builder.Append($$"""{"_class":"hudson.model.FreeStyleBuild","number":{{number}},"url":"https://jenkins.example.com/job/build-all/{{number}}/","result":"SUCCESS","building":false,"timestamp":1767264900000,"duration":290000,"actions":[{"_class":"hudson.model.CauseAction"},{"_class":"hudson.model.ParametersAction","parameters":[""");
            for (var index = 0; index < 25; index++)
            {
                var value = (index % 5) switch
                {
                    0 => "true",
                    1 => index.ToString(CultureInfo.InvariantCulture),
                    2 => "null",
                    3 => "\"a value long enough to be a path, C:\\\\agents\\\\work\\\\build-all\\\\output\"",
                    _ => $"\"option-{index}\""
                };
                if (index == 12)
                {
                    value = "\"Ada Lovelace\"";
                }

                var name = index == 12 ? TriggeredBy.Property : $"PARAMETER_{index}";
                builder.Append($$"""{"_class":"hudson.model.StringParameterValue","name":"{{name}}","value":{{value}}},""");
            }

            builder.Length--;
            builder.Append("""]},{"_class":"hudson.plugins.git.util.BuildData","lastBuiltRevision":{"branch":[{"name":"refs/remotes/origin/main"}]}}]},""");
        }

        builder.Length--;
        builder.Append("""],"lastBuild":{"number":50,"estimatedDuration":300000},"inQueue":false}""");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    static byte[] AzureDevOpsBuilds(int count)
    {
        var builder = new StringBuilder($$"""{"count":{{count}},"value":[""");
        for (var id = count; id > 0; id--)
        {
            var parameters = $$"""{\"system.pullRequest.pullRequestId\":\"{{id}}\",\"system.pullRequest.pullRequestNumber\":\"{{id}}\",\"system.pullRequest.mergedAt\":\"\",\"system.pullRequest.sourceBranch\":\"feature/{{id}}\",\"system.pullRequest.targetBranch\":\"main\",\"system.pullRequest.sourceRepositoryUri\":\"https://github.com/VerifyTests/DiffEngine\",\"system.pullRequest.sourceCommitId\":\"def456\",\"system.pullRequest.isFork\":\"False\"}""";
            builder.Append($$$"""{"id":{{{id}}},"buildNumber":"20260101.{{{id}}}","status":"completed","result":"succeeded","queueTime":"2026-01-01T10:50:00Z","startTime":"2026-01-01T10:51:00Z","finishTime":"2026-01-01T10:59:00Z","sourceBranch":"refs/pull/{{{id}}}/merge","sourceVersion":"def456","reason":"pullRequest","requestedFor":{"displayName":"Simon","id":"e9d8e877-ffad-6366-81bf-b5db2947d44c"},"definition":{"id":1,"name":"CI"},"repository":{"id":"VerifyTests/DiffEngine","type":"GitHub","name":"VerifyTests/DiffEngine"},"properties":{"TriggeredBy":{"$type":"System.String","$value":"Ada Lovelace"}},"parameters":"{{{parameters}}}","triggerInfo":{"pr.sourceBranch":"feature/{{{id}}}","pr.number":"{{{id}}}","pr.isFork":"False"},"_links":{"web":{"href":"https://dev.azure.com/contoso/Web/_build/results?buildId={{{id}}}""").Append("\"}}},");
        }

        builder.Length--;
        builder.Append("]}");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }
}
