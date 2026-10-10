/// <summary>
/// Finds the tray executable for this machine under heads/{rid}. The exact RID is tried first,
/// then the plain os-arch one, the same order DiffEngine resolves its native renderers in.
/// <para>
/// A musl RID is the exception and is tried alone. No head is built for musl, and the os-arch
/// one after it is linux-{arch}, the glibc build: found, it would be started and never answer,
/// so a linux-musl-x64 machine has to miss here to be told there is no head for it.
/// </para>
/// </summary>
static class HeadLocator
{
    public static string? Find(string? baseDirectory = null)
    {
        var overridden = Environment.GetEnvironmentVariable("BuildMonitor_Head");
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return overridden;
        }

        return Find(baseDirectory ?? AppContext.BaseDirectory, Rids(), OperatingSystem.IsWindows());
    }

    public static string? Find(string baseDirectory, IEnumerable<string> rids, bool windows)
    {
        var file = windows ? "BuildMonitor.Tray.exe" : "BuildMonitor.Tray";
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

    public static IEnumerable<string> Rids() =>
        Rids(RuntimeInformation.RuntimeIdentifier);

    public static IEnumerable<string> Rids(string runtimeIdentifier)
    {
        // The exact RID first. On Alpine that is linux-musl-x64, which we do not ship, so the
        // probe misses and the launcher says there is no head for this machine.
        yield return runtimeIdentifier;

        // And nothing else, or the synthesised RID below undoes that: linux-{arch} is the glibc
        // build, an apphost that cannot start on musl. The launcher would start it detached, wait
        // out its timeout and report a tray that did not answer, when the truth is that no head
        // is built for this machine.
        if (runtimeIdentifier.Contains("-musl-", StringComparison.Ordinal))
        {
            yield break;
        }

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
