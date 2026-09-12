record AuthResult(bool Ok, string? AccessToken, string? RefreshToken, string? Error)
{
    public static AuthResult Success(string accessToken, string? refreshToken) =>
        new(true, accessToken, refreshToken, null);

    public static AuthResult Failed(string error) =>
        new(false, null, null, error);
}
