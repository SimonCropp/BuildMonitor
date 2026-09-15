/// <summary>
/// Opens the package a Release build drops in nugets and pins its entry list. Package content
/// is assembled by several unrelated MSBuild mechanisms and nothing else asserts the result;
/// the failure mode this exists for is a head that silently stopped being bundled.
/// </summary>
public class PackageTests
{
    static readonly string nugets = FindNugets();

    static string FindNugets()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "nugets");
            if (Directory.Exists(candidate) &&
                File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return "";
    }

    public static string? Package()
    {
        if (nugets.Length == 0)
        {
            return null;
        }

        return Directory.GetFiles(nugets, "BuildMonitor.*.nupkg")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    [Test]
    [PackageTest]
    public async Task Contents()
    {
        await using var archive = await ZipFile.OpenReadAsync(Package()!);
        var entries = archive.Entries
            .Select(_ => _.FullName)
            .Where(_ => !_.StartsWith("_rels/", StringComparison.Ordinal) && !_.StartsWith("package/", StringComparison.Ordinal) && _ != "[Content_Types].xml")
            .Select(_ => Regex.Replace(_, @"BuildMonitor\.\d+\.\d+\.\d+[^/]*\.nuspec", "BuildMonitor.{version}.nuspec"))
            .Order(StringComparer.Ordinal)
            .ToList();
        await Verify(entries);
    }

    [Test]
    [PackageTest]
    public async Task EveryRidHasAHead()
    {
        await using var archive = await ZipFile.OpenReadAsync(Package()!);
        var heads = archive.Entries
            .Select(_ => _.FullName)
            .Where(_ => _.StartsWith("tools/net10.0/any/heads/", StringComparison.Ordinal) && _.Contains("/BuildMonitor.Tray", StringComparison.Ordinal) && !_.EndsWith(".dll", StringComparison.Ordinal) && !_.EndsWith(".json", StringComparison.Ordinal))
            .ToList();
        await Assert.That(heads).Contains("tools/net10.0/any/heads/win-x64/BuildMonitor.Tray.exe");
        await Assert.That(heads).Contains("tools/net10.0/any/heads/win-arm64/BuildMonitor.Tray.exe");
    }

    [Test]
    [PackageTest]
    public async Task NoPdbsShip()
    {
        await using var archive = await ZipFile.OpenReadAsync(Package()!);
        await Assert.That(archive.Entries.Any(_ => _.FullName.EndsWith(".pdb", StringComparison.Ordinal))).IsFalse();
    }
}