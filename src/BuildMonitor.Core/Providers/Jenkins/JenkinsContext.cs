[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, Converters = [typeof(PooledStringConverter)])]
[JsonSerializable(typeof(JenkinsNode))]
[JsonSerializable(typeof(JenkinsJob))]
[JsonSerializable(typeof(JenkinsArtifacts))]
[JsonSerializable(typeof(JenkinsCrumb))]
[JsonSerializable(typeof(JenkinsUser))]
partial class JenkinsContext : JsonSerializerContext;
