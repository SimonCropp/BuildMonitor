using ModelContextProtocol.Server;

/// <summary>
/// Pins the tool surface an assistant sees: names, descriptions and parameters.
/// </summary>
public class BuildToolsTests
{
    [Test]
    public Task ToolSurface()
    {
        var tools = typeof(BuildTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(_ => new
            {
                _.GetCustomAttribute<McpServerToolAttribute>()!.Name,
                Description = _.GetCustomAttribute<DescriptionAttribute>()?.Description,
                Parameters = _.GetParameters()
                    .Where(parameter => parameter.ParameterType != typeof(Cancel))
                    .Select(parameter => new
                    {
                        parameter.Name,
                        Type = parameter.ParameterType.Name,
                        Description = parameter.GetCustomAttribute<DescriptionAttribute>()?.Description
                    })
            })
            .OrderBy(_ => _.Name);
        return Verify(tools);
    }

    [Test]
    public async Task EveryToolIsDescribed()
    {
        var methods = typeof(BuildTools).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        await Assert.That(methods.All(_ => _.GetCustomAttribute<DescriptionAttribute>() is not null)).IsTrue();
        await Assert.That(methods.Length).IsEqualTo(9);
    }
}
