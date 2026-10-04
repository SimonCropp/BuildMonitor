/// <summary>
/// The token endpoint's answer, success or error, in the shape every provider here uses.
/// </summary>
class TokenResponse
{
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public int? ExpiresIn { get; init; }
    public string? Error { get; init; }
    public string? ErrorDescription { get; init; }
    public int? Interval { get; init; }
}

class DeviceCodeResponse
{
    public string? DeviceCode { get; init; }
    public string? UserCode { get; init; }
    public string? VerificationUri { get; init; }
    public string? VerificationUriComplete { get; init; }
    public int? ExpiresIn { get; init; }
    public int? Interval { get; init; }
    public string? Error { get; init; }
    public string? ErrorDescription { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(DeviceCodeResponse))]
partial class OAuthContext : JsonSerializerContext;
