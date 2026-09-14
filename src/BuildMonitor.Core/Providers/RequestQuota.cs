/// <summary>
/// A limit the service enforces whether or not its headers say so, kept as a token bucket.
/// <para>
/// Bitbucket allows a thousand requests an hour and charges one per repository, so a workspace of
/// fifty repositories polled every minute used its hour in twenty minutes. GitHub's secondary
/// limit counts requests a minute, 304s included. Azure DevOps counts throughput units rather
/// than requests, so its quota is charged by each response's X-RateLimit-Cost.
/// </para>
/// </summary>
/// <param name="LearnLimit">Whether X-RateLimit-Limit replaces <see cref="Requests"/>, for
/// Bitbucket workspaces with a scaled limit.</param>
record RequestQuota(double Requests, TimeSpan Per, double Burst, bool LearnLimit = false, bool ChargeByCost = false);
