/// <summary>
/// The credential was refused. Polling stops for the connection until the user signs in again,
/// because a dead token does not come back on its own.
/// </summary>
sealed class AuthException(string message) : Exception(message);
