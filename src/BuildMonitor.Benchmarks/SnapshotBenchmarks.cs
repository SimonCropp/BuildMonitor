/// <summary>
/// What the MCP tools read, each a pass over every build: the pipelines with how many runs each
/// has, a build by its key, and a pipeline's runs by its key. The keys are the last pipeline's,
/// so a search walks every build before it.
/// </summary>
[MemoryDiagnoser]
public class SnapshotBenchmarks
{
    SessionState state = LargeAccount.State();
    string buildKey = LargeAccount.Builds()[^1].Key;
    string pipelineKey = LargeAccount.Builds()[^1].PipelineKey;

    [Benchmark]
    public object Pipelines() =>
        Snapshot.Pipelines(state);

    [Benchmark]
    public object? Find() =>
        Snapshot.Find(state, buildKey, LargeAccount.Now);

    [Benchmark]
    public object? Runs() =>
        Snapshot.Runs(state, pipelineKey, LargeAccount.Now);
}
