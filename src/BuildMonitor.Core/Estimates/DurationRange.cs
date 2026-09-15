/// <summary>
/// The fastest and the slowest of a pipeline's recent successful runs: the stretch of a run in which
/// it can be expected to finish. A median alone put a pipeline that always takes ten minutes and one
/// that takes anywhere from five to twenty on the same footing.
/// </summary>
readonly record struct DurationRange(TimeSpan Fastest, TimeSpan Slowest);
