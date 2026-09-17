/// <summary>
/// Whether the tray icon sits on the taskbar or in the overflow behind the chevron. Windows 11
/// records that per executable path, and puts an icon from a path it has not seen in the
/// overflow. The head runs from a versioned directory in the tool store, so every update is a path
/// Windows has not seen, and an icon the user had put on the taskbar went back into the overflow.
/// <para>
/// So a path with no decision takes the one recorded for the newest other version of the head,
/// or the taskbar when no version has one. A recorded decision is never changed: an icon the user
/// moved to the overflow stays there, and through this, stays there after an update too.
/// </para>
/// </summary>
static class TrayIconPromotion
{
    /// <summary>
    /// The entries to write, each with the decision to record; empty when every entry for
    /// <paramref name="processPath"/> has one. Null while Windows has no entry for it, which it
    /// adds some time after the icon.
    /// </summary>
    public static IReadOnlyList<NotifyIconEntry>? Decide(IReadOnlyList<NotifyIconEntry> entries, string processPath)
    {
        var own = entries
            .Where(_ => SamePath(_.Path, processPath))
            .ToList();
        if (own.Count == 0)
        {
            return null;
        }

        var undecided = own
            .Where(_ => _.Promoted is null)
            .ToList();
        if (undecided.Count == 0)
        {
            return [];
        }

        var promoted = Inherited(entries, processPath) ?? true;
        return undecided
            .Select(_ => _ with { Promoted = promoted })
            .ToList();
    }

    /// <summary>
    /// A build run from outside the store has no versions to inherit from.
    /// </summary>
    static bool? Inherited(IReadOnlyList<NotifyIconEntry> entries, string processPath)
    {
        if (ToolStorePath.Parse(processPath) is not { } current)
        {
            return null;
        }

        string? newest = null;
        bool? promoted = null;
        foreach (var entry in entries)
        {
            if (entry.Promoted is null ||
                SamePath(entry.Path, processPath) ||
                ToolStorePath.Parse(entry.Path) is not { } other ||
                !other.SameFile(current))
            {
                continue;
            }

            if (newest is null ||
                PackageVersion.Compare(other.Version, newest) > 0)
            {
                newest = other.Version;
                promoted = entry.Promoted;
            }
        }

        return promoted;
    }

    static bool SamePath(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
