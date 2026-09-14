/// <summary>
/// A token bucket over a <see cref="RequestQuota"/>. Spending can go below zero, because what a
/// fetch cost is only known afterwards: a GitLab project with running pipelines costs more than one
/// request, and an Azure DevOps request costs whatever its X-RateLimit-Cost says. Debt holds back
/// the next fetch until it is repaid.
/// </summary>
readonly record struct RequestBucket(double Tokens, DateTimeOffset UpdatedAt)
{
    public static RequestBucket Full(RequestQuota quota, DateTimeOffset now) =>
        new(quota.Burst, now);

    public RequestBucket Refill(RequestQuota quota, DateTimeOffset now)
    {
        if (now <= UpdatedAt)
        {
            return this;
        }

        var tokens = Math.Min(quota.Burst, Tokens + (now - UpdatedAt).TotalSeconds * Rate(quota));
        return new(tokens, now);
    }

    public RequestBucket Spend(double requests) =>
        this with { Tokens = Tokens - requests };

    /// <summary>
    /// When the bucket next holds a whole token.
    /// </summary>
    public DateTimeOffset NextToken(RequestQuota quota) =>
        Tokens >= 1
            ? UpdatedAt
            : UpdatedAt + TimeSpan.FromSeconds((1 - Tokens) / Rate(quota));

    static double Rate(RequestQuota quota) =>
        quota.Requests / quota.Per.TotalSeconds;
}
