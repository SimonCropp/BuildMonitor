/// <summary>
/// What a provider says about how far along a running build is. Any field may be missing:
/// Jenkins gives a duration, TeamCity and Octopus give a percentage and seconds remaining, Azure
/// DevOps gives a percentage. Whatever is present beats the history-derived guess.
/// </summary>
record ProviderEstimate(TimeSpan? Duration, double? Percent, TimeSpan? Remaining);
