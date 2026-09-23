/// <summary>
/// The default branches that were asked for their newest run lately and had none to show: nothing
/// built on them, or nothing since the history cutoff. A repository whose default branch is quiet
/// while its pull requests are busy would otherwise pay a request for that run on every poll to
/// learn the same thing. Asked again after an hour, and never sooner is needed: a new run on the
/// branch is the pipeline's newest, so it arrives in the ordinary window first.
/// <para>
/// Keyed by pipeline and branch, because a connection's pipelines are fetched concurrently and a
/// pipeline whose default branch changes should be asked about the new one at once.
/// </para>
/// </summary>
static class DefaultRunMemory
{
    static TimeSpan retry = TimeSpan.FromHours(1);

    static string Key(string pipelineId, string branch) =>
        $"default-run.none|{pipelineId}|{branch}";

    public static bool KnownNone(ProviderMemory memory, string pipelineId, string branch, DateTimeOffset now) =>
        memory.TryGet<DateTimeOffset>(Key(pipelineId, branch), out var asked) &&
        now - asked < retry;

    public static void None(ProviderMemory memory, string pipelineId, string branch, DateTimeOffset now) =>
        memory.Set(Key(pipelineId, branch), now);

    public static void Found(ProviderMemory memory, string pipelineId, string branch) =>
        memory.Remove(Key(pipelineId, branch));
}
