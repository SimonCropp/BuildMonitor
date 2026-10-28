[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true, Converters = [typeof(PooledStringConverter)])]
[JsonSerializable(typeof(GoCdDashboard))]
[JsonSerializable(typeof(GoCdHistory))]
[JsonSerializable(typeof(GoCdInstance))]
[JsonSerializable(typeof(List<GoCdArtifactEntry>))]
[JsonSerializable(typeof(GoCdUser))]
partial class GoCdContext : JsonSerializerContext;
