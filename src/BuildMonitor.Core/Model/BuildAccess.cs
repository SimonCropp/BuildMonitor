/// <summary>
/// What a connection's credential may do to builds, where the service says so without a change
/// being tried. Retry and Cancel used to be offered on every row, and a token that could only read
/// found out from a refusal after the click.
/// </summary>
enum BuildAccess
{
    // The service does not say, or has not been asked. Retry and cancel are offered, and a refusal
    // says what the connection needs.
    Unknown,
    // The credential can only read, so no build of the connection offers retry or cancel.
    Watch,
    // The credential may retry and cancel. A pipeline its user may only watch can still refuse, so
    // a provider that knows its user's rights on each pipeline narrows this per build.
    Change
}
