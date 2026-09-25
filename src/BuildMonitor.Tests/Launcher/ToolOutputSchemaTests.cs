using ModelContextProtocol.Server;

/// <summary>
/// Every structured tool's result against the output schema it advertises. A client validates the
/// one against the other and rejects the whole call on a mismatch, so a member the schema requires
/// but the serializer leaves out, as every null used to be, loses the tool rather than the member.
/// The results are built with every member that can be null set to null, the shape most likely to
/// drop one, and through the same options the server hands the SDK.
/// </summary>
public class ToolOutputSchemaTests
{
    static NullabilityInfoContext nullability = new();

    [Test]
    public async Task StructuredContentSatisfiesTheOutputSchema()
    {
        var tools = typeof(BuildTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(_ => _.GetCustomAttribute<McpServerToolAttribute>()!.UseStructuredContent)
            .ToList();
        var violations = new List<string>();
        foreach (var method in tools)
        {
            var tool = McpServerTool.Create(
                method,
                _ => throw new InvalidOperationException(),
                new()
                {
                    SerializerOptions = McpHost.ToolSerializerOptions
                });
            var schema = tool.ProtocolTool.OutputSchema!.Value;
            var resultType = method.ReturnType.GetGenericArguments().Single();
            var content = JsonSerializer.SerializeToElement(Sample(resultType), resultType, McpHost.ToolSerializerOptions);
            violations.AddRange(Violations(schema, content, tool.ProtocolTool.Name));
        }

        await Assert.That(tools).IsNotEmpty();
        await Assert.That(violations).IsEmpty();
    }

    /// <summary>
    /// A member the record makes optional is left out when null rather than written as one: the
    /// schema does not require it, and a null directory on every build would be noise.
    /// </summary>
    [Test]
    public async Task OptionalMembersAreLeftOutWhenNull()
    {
        var build = JsonSerializer.SerializeToElement(Sample(typeof(BuildDto)), typeof(BuildDto), McpHost.ToolSerializerOptions);
        await Assert.That(build.GetProperty("pullRequest").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(build.TryGetProperty("directory", out _)).IsFalse();
        await Assert.That(build.TryGetProperty("otherBranch", out _)).IsFalse();
        await Assert.That(build.TryGetProperty("deferredUntil", out _)).IsFalse();
    }

    /// <summary>
    /// A value of the type with every member that can be null set to null, and one element in every
    /// list.
    /// </summary>
    static object? Sample(Type type)
    {
        if (type == typeof(string))
        {
            return "text";
        }

        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }

        if (type.IsGenericType &&
            typeof(IEnumerable).IsAssignableFrom(type) &&
            type.GetGenericArguments() is [var elementType])
        {
            var list = (IList) Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
            list.Add(Sample(elementType));
            return list;
        }

        var constructor = type.GetConstructors().Single();
        return constructor.Invoke(constructor.GetParameters().Select(Argument).ToArray());
    }

    static object? Argument(ParameterInfo parameter)
    {
        if (nullability.Create(parameter).WriteState == NullabilityState.Nullable)
        {
            return null;
        }

        return Sample(parameter.ParameterType);
    }

    /// <summary>
    /// The parts of JSON Schema the generated output schemas use: type, required, properties and
    /// items.
    /// </summary>
    static IEnumerable<string> Violations(JsonElement schema, JsonElement instance, string path)
    {
        if (!Allows(schema, instance.ValueKind))
        {
            yield return $"{path} is {instance.ValueKind}, which its schema does not allow";
            yield break;
        }

        if (instance.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("required", out var required))
            {
                foreach (var name in required.EnumerateArray().Select(_ => _.GetString()!))
                {
                    if (!instance.TryGetProperty(name, out _))
                    {
                        yield return $"{path} is missing required {name}";
                    }
                }
            }

            if (schema.TryGetProperty("properties", out var properties))
            {
                foreach (var property in instance.EnumerateObject())
                {
                    if (!properties.TryGetProperty(property.Name, out var propertySchema))
                    {
                        yield return $"{path}.{property.Name} is not in its schema";
                        continue;
                    }

                    foreach (var violation in Violations(propertySchema, property.Value, $"{path}.{property.Name}"))
                    {
                        yield return violation;
                    }
                }
            }
        }

        if (instance.ValueKind == JsonValueKind.Array &&
            schema.TryGetProperty("items", out var items))
        {
            var index = 0;
            foreach (var element in instance.EnumerateArray())
            {
                foreach (var violation in Violations(items, element, $"{path}[{index}]"))
                {
                    yield return violation;
                }

                index++;
            }
        }
    }

    static bool Allows(JsonElement schema, JsonValueKind kind)
    {
        if (!schema.TryGetProperty("type", out var type))
        {
            return true;
        }

        var names = TypeNames(type).ToList();
        return kind switch
        {
            JsonValueKind.Null => names.Contains("null"),
            JsonValueKind.String => names.Contains("string"),
            JsonValueKind.Number => names.Contains("number") || names.Contains("integer"),
            JsonValueKind.True or JsonValueKind.False => names.Contains("boolean"),
            JsonValueKind.Array => names.Contains("array"),
            JsonValueKind.Object => names.Contains("object"),
            _ => false
        };
    }

    static IEnumerable<string?> TypeNames(JsonElement type)
    {
        if (type.ValueKind == JsonValueKind.Array)
        {
            return type.EnumerateArray().Select(_ => _.GetString());
        }

        return [type.GetString()];
    }
}
