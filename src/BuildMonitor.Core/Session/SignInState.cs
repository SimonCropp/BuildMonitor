/// <summary>
/// A browser or device sign in that is under way. <see cref="FlowId"/> ties the asynchronous
/// completion back to this attempt, so a result from a flow the user already cancelled is
/// ignored rather than signing in a connection they moved on from.
/// </summary>
record SignInState(
    Connection Connection,
    AuthMethod Method,
    Guid FlowId,
    string Message,
    string? UserCode,
    string? VerificationUrl);
