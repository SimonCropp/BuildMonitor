/// <summary>
/// Finds the tray executable for this machine under heads/{rid}. The exact RID is tried first,
/// then the plain os-arch one, the same order DiffEngine resolves its native renderers in, so
/// a linux-musl-x64 machine misses rather than starting a glibc build.
/// </summary>
static class HeadLocator
{
    public const string Variable = "BuildMonitor_Head";
    public const string FileName = "BuildMonitor.Tray";

    public static string? Find(string? baseDirectory = null)
    {
        var overridden = Environment.GetEnvironmentVariable(Variable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return overridden;
        }

        return Find(baseDirectory ?? AppContext.BaseDirectory, Rids(), OperatingSystem.IsWindows());
    }

    public static string? Find(string baseDirectory, IEnumerable<string> rids, bool windows)
    {
        var file = windows ? $"{FileName}.exe" : FileName;
        foreach (var rid in rids)
        {
            var candidate = Path.Combine(baseDirectory, "heads", rid, file);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static IEnumerable<string> Rids()
    {
        yield return RuntimeInformation.RuntimeIdentifier;
        var architecture = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => null
        };
        if (architecture is null)
        {
            yield break;
        }

        if (OperatingSystem.IsWindows())
        {
            yield return $"win-{architecture}";
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return $"osx-{architecture}";
        }
        else if (OperatingSystem.IsLinux())
        {
            yield return $"linux-{architecture}";
        }
    }
}
