/// <summary>
/// Where the countdown beside a running build came from. The row shows the number and not its
/// origin, and the two are worth telling apart: a provider's own estimate is about this run, and a
/// median is only about the ones before it.
/// </summary>
enum EstimateSource
{
    None,
    Provider,
    History
}
