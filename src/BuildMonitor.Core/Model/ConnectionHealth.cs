enum ConnectionHealth
{
    Unpolled,
    Ok,
    Polling,
    Error,
    // 401 or 403: polling stops until the connection is signed in again, and the tray asks for
    // attention, because polling a dead token every thirty seconds only produces log noise.
    NeedsAuth,
    RateLimited
}
