/// <summary>
/// Asks the window server for the session's dictionary, which carries CGSSessionScreenIsLocked
/// while the screen is locked and leaves it out otherwise. The notifications for it,
/// com.apple.screenIsLocked and screenIsUnlocked, arrive only on a thread running a run loop.
/// </summary>
[SupportedOSPlatform("macos")]
sealed partial class MacSessionLock : ISessionLock
{
    const string coreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    const string coreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    const uint utf8 = 0x08000100;

    public ValueTask<bool?> IsLocked(Cancel cancel) =>
        ValueTask.FromResult(Read());

    static bool? Read()
    {
        // Null outside a GUI session, as over ssh.
        var session = CGSessionCopyCurrentDictionary();
        if (session == 0)
        {
            return null;
        }

        var key = CFStringCreateWithCString(0, "CGSSessionScreenIsLocked", utf8);
        try
        {
            var value = CFDictionaryGetValue(session, key);
            return value != 0 && CFBooleanGetValue(value);
        }
        finally
        {
            CFRelease(key);
            CFRelease(session);
        }
    }

    [LibraryImport(coreGraphics)]
    private static partial nint CGSessionCopyCurrentDictionary();

    [LibraryImport(coreFoundation, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint CFStringCreateWithCString(nint allocator, string text, uint encoding);

    [LibraryImport(coreFoundation)]
    private static partial nint CFDictionaryGetValue(nint dictionary, nint key);

    [LibraryImport(coreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFBooleanGetValue(nint boolean);

    [LibraryImport(coreFoundation)]
    private static partial void CFRelease(nint value);
}
