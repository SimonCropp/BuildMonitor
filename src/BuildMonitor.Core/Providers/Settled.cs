/// <summary>
/// The outcome of one item of <see cref="Concurrently.Settle{TItem,TResult}"/>: its value, or what
/// it threw.
/// </summary>
readonly record struct Settled<TResult>(TResult? Value, Exception? Exception);
