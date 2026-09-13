[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TeamCityBuildTypes))]
[JsonSerializable(typeof(TeamCityBuilds))]
[JsonSerializable(typeof(TeamCityServer))]
[JsonSerializable(typeof(TeamCityQueueRequest))]
[JsonSerializable(typeof(TeamCityCancel))]
partial class TeamCityContext : JsonSerializerContext;
