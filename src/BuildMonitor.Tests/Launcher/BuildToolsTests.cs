using ModelContextProtocol.Server;

/// <summary>
/// Pins the tool surface an assistant sees: names, descriptions and parameters.
/// </summary>
public class BuildToolsTests
{
    static MethodInfo[] methods = typeof(BuildTools)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    [Test]
    public Task ToolSurface()
    {
        var tools = methods
            .Select(_ => new
            {
                _.GetCustomAttribute<McpServerToolAttribute>()!.Name,
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
        return Verify(tools);
    }

    [Test]
    public async Task EveryToolIsDescribed()
    {
        await Assert.That(methods.All(_ => _.GetCustomAttribute<DescriptionAttribute>() is not null)).IsTrue();
        await Assert.That(methods.Length).IsEqualTo(9);
    }
}
