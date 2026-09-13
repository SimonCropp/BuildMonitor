/// <summary>
/// A Debug build produces no package, and there is nothing to assert about that.
/// </summary>
public sealed class PackageTestAttribute() :
    SkipAttribute("No package in nugets; run a Release build first.")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(PackageTests.Package() is null);
}