[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(TravisRepositories))]
[JsonSerializable(typeof(TravisBuilds))]
[JsonSerializable(typeof(TravisUser))]
partial class TravisContext : JsonSerializerContext;
