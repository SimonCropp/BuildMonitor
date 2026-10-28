[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true, Converters = [typeof(PooledStringConverter)])]
[JsonSerializable(typeof(TravisRepositories))]
[JsonSerializable(typeof(TravisBuilds))]
[JsonSerializable(typeof(TravisJobs))]
[JsonSerializable(typeof(TravisUser))]
partial class TravisContext : JsonSerializerContext;
