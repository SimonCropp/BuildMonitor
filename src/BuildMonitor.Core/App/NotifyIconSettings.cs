using Microsoft.Win32;

/// <summary>
/// HKCU\Control Panel\NotifyIconSettings, where Windows 11 keeps a subkey for every tray icon it has
/// seen: the executable path that added it, and IsPromoted once there is a decision. Explorer
/// watches the key, so a value written here moves the icon at once. Earlier versions of Windows
/// have no such key, and there this does nothing.
/// </summary>
[SupportedOSPlatform("windows")]
static partial class NotifyIconSettings
{
    const string keyPath = @"Control Panel\NotifyIconSettings";
    const string promotedName = "IsPromoted";
    static readonly TimeSpan interval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Windows adds the entry some time after the icon, and at login the icon itself can be waiting
    /// for the taskbar, so the entry is looked for until it appears or a minute has passed.
    /// </summary>
    static readonly TimeSpan giveUp = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Records the <see cref="TrayIconPromotion"/> decision for the icon of
    /// <paramref name="processPath"/>, off the caller's thread. A failure is logged and otherwise
    /// ignored: the icon still works in the overflow.
    /// </summary>
    public static void Apply(string processPath) =>
        _ = Task.Run(async () =>
        {
            try
            {
                for (var waited = TimeSpan.Zero; waited < giveUp; waited += interval)
                {
                    if (TryApply(processPath))
                    {
                        return;
                    }

                    await Task.Delay(interval);
                }

                Log.Information("Windows has no tray icon entry for {Path}, so it decides where the icon shows", processPath);
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Could not decide where the tray icon shows");
            }
        });

    /// <summary>
    /// False while Windows has no entry for <paramref name="processPath"/>.
    /// </summary>
    static bool TryApply(string processPath)
    {
        var entries = Read();
        if (entries is null)
        {
            return true;
        }

        var decided = TrayIconPromotion.Decide(entries, processPath);
        if (decided is null)
        {
            return false;
        }

        foreach (var entry in decided)
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{keyPath}\{entry.Key}", true);
            if (key is null)
            {
                continue;
            }

            var promoted = entry.Promoted == true;
            key.SetValue(promotedName, promoted ? 1 : 0, RegistryValueKind.DWord);
            Log.Information("Tray icon entry {Key} for {Path} recorded as {Placement}", entry.Key, entry.Path, promoted ? "on the taskbar" : "in the overflow");
        }

        return true;
    }

    /// <summary>
    /// Null where Windows has no such key.
    /// </summary>
    static List<NotifyIconEntry>? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        if (key is null)
        {
            return null;
        }

        var entries = new List<NotifyIconEntry>();
        foreach (var name in key.GetSubKeyNames())
        {
            using var entry = key.OpenSubKey(name);
            if (entry?.GetValue("ExecutablePath") is not string path)
            {
                continue;
            }

            bool? promoted = entry.GetValue(promotedName) is int value ? value != 0 : null;
            entries.Add(new(name, Expand(path), promoted));
        }

        return entries;
    }

    /// <summary>
    /// Windows records a path under a known folder by the folder's id, so one under Program Files
    /// reads {6D809377-6AF0-444B-8957-A3773F02200E}\Vendor\App.exe. A path under the user profile,
    /// where the tool store usually is, is recorded as it is.
    /// </summary>
    public static string Expand(string path)
    {
        var end = path.IndexOf('}');
        if (!path.StartsWith('{') ||
            end < 0 ||
            !Guid.TryParse(path.AsSpan(0, end + 1), out var id) ||
            SHGetKnownFolderPath(in id, 0, 0, out var folder) < 0)
        {
            return path;
        }

        return folder + path[(end + 1)..];
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHGetKnownFolderPath(in Guid id, uint flags, nint token, out string path);
}
