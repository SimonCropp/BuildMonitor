[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(JenkinsNode))]
[JsonSerializable(typeof(JenkinsJob))]
[JsonSerializable(typeof(JenkinsCrumb))]
[JsonSerializable(typeof(JenkinsUser))]
partial class JenkinsContext : JsonSerializerContext;
