/// <summary>
/// The launcher is a GUI subsystem executable, so the tool shim opens no console at login. A
/// verb that prints therefore attaches to the console it was started from, when there is one.
/// </summary>
static partial class ConsoleAttach
{
    const int attachParentProcess = -1;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);

    public static void TryAttachParent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            AttachConsole(attachParentProcess);
        }
        catch (Exception)
        {
            // No console to attach to; the output goes nowhere, which is what a GUI launch wants.
        }
    }
}
