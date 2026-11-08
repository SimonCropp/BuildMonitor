using ModelContextProtocol;
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
                    // Bound by the SDK rather than given by the assistant, and absent from the schema.
                    .Where(_ => _.ParameterType != typeof(Cancel) &&
                                _.ParameterType != typeof(IProgress<ProgressNotificationValue>))
                    .Select(_ => new
                    {
                        _.Name,
                        Type = _.ParameterType.Name,
                        _.GetCustomAttribute<DescriptionAttribute>()?.Description
                    })
            })
            .OrderBy(_ => _.Name);
        return Verify(tools);
    }

    /// <summary>
    /// What the SDK binds itself, the cancellation and the progress reporter, stays out of the
    /// input schema the surface above stands for: an assistant shown either would try to pass it.
    /// </summary>
    [Test]
    public async Task TheInputSchemaHoldsOnlyWhatTheAssistantPasses()
    {
        var mismatched = new List<string>();
        foreach (var method in methods)
        {
            var tool = McpServerTool.Create(
                method,
                _ => throw new InvalidOperationException(),
                new()
                {
                    SerializerOptions = McpHost.ToolSerializerOptions
                });
            List<string> inSchema = [];
            if (tool.ProtocolTool.InputSchema.TryGetProperty("properties", out var properties))
            {
                inSchema = [.. properties.EnumerateObject().Select(_ => _.Name)];
            }

            var passed = method.GetParameters()
                .Where(_ => _.ParameterType != typeof(Cancel) &&
                            _.ParameterType != typeof(IProgress<ProgressNotificationValue>))
                .Select(_ => _.Name!);
            if (!inSchema.SequenceEqual(passed))
            {
                mismatched.Add($"{tool.ProtocolTool.Name}: {string.Join(", ", inSchema)}");
            }
        }

        await Assert.That(mismatched).IsEmpty();
    }

    [Test]
    public async Task EveryToolIsDescribed()
    {
        await Assert.That(methods.All(_ => _.GetCustomAttribute<DescriptionAttribute>() is not null)).IsTrue();
        await Assert.That(methods.Length).IsEqualTo(16);
    }
}
