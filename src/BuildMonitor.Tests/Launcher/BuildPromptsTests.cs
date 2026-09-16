using ModelContextProtocol.Server;

/// <summary>
/// Pins the prompt surface a client shows the user: names, descriptions and arguments. The tool
/// surface test reflects over <see cref="BuildTools"/> alone, so without this a prompt is pinned by
/// nothing and could be renamed out from under everyone who has it in a slash command.
/// </summary>
public class BuildPromptsTests
{
    static MethodInfo[] methods = typeof(BuildPrompts)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    [Test]
    public Task PromptSurface()
    {
        var prompts = methods
            .Select(_ => new
            {
                _.GetCustomAttribute<McpServerPromptAttribute>()!.Name,
                _.GetCustomAttribute<DescriptionAttribute>()?.Description,
                Parameters = _.GetParameters()
                    .Where(_ => _.ParameterType != typeof(Cancel))
                    .Select(parameter => new
                    {
                        parameter.Name,
                        Type = parameter.ParameterType.Name,
                        parameter.GetCustomAttribute<DescriptionAttribute>()?.Description
                    })
            })
            .OrderBy(_ => _.Name);
        return Verify(prompts)
            .Snapshot(
                """
                [
                  {
                    Name: triage,
                    Description: Work through the builds failing now whose repository is checked out locally: read their logs, group the ones that share a cause, and reproduce each from its checkout. Covers every configured connection.,
                    Parameters: [
                      {
                        Name: filter,
                        Type: String,
                        Description: Optional substring to filter on pipeline, repository, branch or connection name, as list_builds takes. Left empty, or given as *, every failing build is covered.
                      },
                      {
                        Name: fix,
                        Type: String,
                        Description: Pass true to have each failure fixed where it was reproduced and the tests run, with the changes left uncommitted. Anything else, or nothing, diagnoses and reports the failures and changes no source files.
                      }
                    ]
                  }
                ]
                """);
    }

    [Test]
    public async Task EveryPromptIsDescribed()
    {
        await Assert.That(methods.All(_ => _.GetCustomAttribute<DescriptionAttribute>() is not null)).IsTrue();
        await Assert.That(methods.Length).IsEqualTo(1);
    }
}
