public sealed class NativeTestAttribute() : SkipAttribute("No native renderer is shipped for this RID.")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(!NativeResolver.TryFind(out _));
}
