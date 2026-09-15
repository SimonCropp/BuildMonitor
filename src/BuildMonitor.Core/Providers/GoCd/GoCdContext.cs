[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(GoCdDashboard))]
[JsonSerializable(typeof(GoCdHistory))]
[JsonSerializable(typeof(GoCdInstance))]
[JsonSerializable(typeof(GoCdUser))]
partial class GoCdContext : JsonSerializerContext;
