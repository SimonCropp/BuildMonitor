/// <summary>
/// Whether the desktop has an app for links of a scheme, asked the way each desktop picks the app
/// that opens one, so the answer is about the app a link would reach rather than about files that
/// happen to be on disk.
/// </summary>
static partial class LinkHandlers
{
    /// <summary>
    /// False, rather than thrown, when the desktop cannot be asked: the answer only decides whether
    /// a prompt is offered to an app as well as copied, and a desktop that cannot say has the copy.
    /// </summary>
    public static bool Exists(string scheme)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return Windows(scheme);
            }

            if (OperatingSystem.IsMacOS())
            {
                return Mac(scheme);
            }

            return Linux(scheme);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Could not ask which app opens {Scheme} links", scheme);
            return false;
        }
    }

    // ASSOCF_IS_PROTOCOL: the association is a link's scheme, not a file extension.
    const int assocIsProtocol = 0x1000;
    const int assocFriendlyAppName = 4;

    /// <summary>
    /// Asks for the app's name rather than its command. A packaged app, as the Claude app is, has
    /// a name but no command, so asking for the command finds nothing for it.
    /// </summary>
    [SupportedOSPlatform("windows")]
    static bool Windows(string scheme)
    {
        var length = 0;
        // With no buffer this only says how long the name would be: S_FALSE where there is one,
        // and an error where nothing opens the scheme.
        return AssocQueryString(assocIsProtocol, assocFriendlyAppName, scheme, null, 0, ref length) >= 0;
    }

    [LibraryImport("shlwapi.dll", EntryPoint = "AssocQueryStringW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int AssocQueryString(int flags, int what, string association, string? extra, nint output, ref int length);

    const string coreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    const string coreServices = "/System/Library/Frameworks/CoreServices.framework/CoreServices";
    const uint utf8 = 0x08000100;
    const uint rolesAll = 0xFFFFFFFF;

    /// <summary>
    /// The app Launch Services would open the link with, which is the one <c>open</c> starts.
    /// </summary>
    [SupportedOSPlatform("macos")]
    static bool Mac(string scheme)
    {
        var text = CFStringCreateWithCString(0, $"{scheme}://", utf8);
        if (text == 0)
        {
            return false;
        }

        try
        {
            var url = CFURLCreateWithString(0, text, 0);
            if (url == 0)
            {
                return false;
            }

            try
            {
                var app = LSCopyDefaultApplicationURLForURL(url, rolesAll, 0);
                if (app == 0)
                {
                    return false;
                }

                CFRelease(app);
                return true;
            }
            finally
            {
                CFRelease(url);
            }
        }
        finally
        {
            CFRelease(text);
        }
    }

    [LibraryImport(coreFoundation, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint CFStringCreateWithCString(nint allocator, string text, uint encoding);

    [LibraryImport(coreFoundation)]
    private static partial nint CFURLCreateWithString(nint allocator, nint text, nint baseUrl);

    [LibraryImport(coreServices)]
    private static partial nint LSCopyDefaultApplicationURLForURL(nint url, uint roles, nint error);

    [LibraryImport(coreFoundation)]
    private static partial void CFRelease(nint value);

    /// <summary>
    /// The desktop entry <c>xdg-open</c> would hand the link to, which is what opens it. Without
    /// xdg-utils nothing opens a link at all, so its absence is an honest no.
    /// </summary>
    static bool Linux(string scheme)
    {
        using var process = Process.Start(
            new ProcessStartInfo("xdg-mime", ["query", "default", $"x-scheme-handler/{scheme}"])
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
        if (process is null)
        {
            return false;
        }

        // A desktop entry's name, or nothing: small enough that the pipe never fills while waiting.
        if (!process.WaitForExit(TimeSpan.FromSeconds(5)))
        {
            process.Kill();
            return false;
        }

        return process.ExitCode == 0 &&
               process.StandardOutput.ReadToEnd().Trim().Length > 0;
    }
}
