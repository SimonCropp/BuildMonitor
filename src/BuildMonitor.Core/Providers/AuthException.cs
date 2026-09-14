/// <summary>
/// The credential was refused. Polling stops for the connection until the user signs in again,
/// because a dead token does not come back on its own. <see cref="Status"/> tells a refused
/// token, a 401, from a resource the token may not see, a 403.
/// </summary>
sealed class AuthException(string message, HttpStatusCode status = HttpStatusCode.Unauthorized) :
    Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}
