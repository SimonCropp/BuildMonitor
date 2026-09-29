/// <summary>
/// The WinForms <see cref="IMonitorWindow"/>. Pumped, not inverted: <see cref="MonitorProgram"/>
/// owns the loop, so a frame is one <see cref="Application.DoEvents"/> rather than
/// <see cref="Application.Run()"/>. That is what keeps the loop shared with the native heads.
/// DoEvents is safe here because the state is behind <see cref="SessionHost"/>. The one thing that
/// nests a loop is <see cref="PickDirectory"/>, and it is called from the applier rather than from a
/// paint: the frame loop is simply stopped while the chooser is up, which is what a modal means.
/// </summary>
sealed class FormsMonitorWindow : IMonitorWindow
{
    MonitorForm form;
    bool disposed;
    // When the last Wait ended, which the next one keeps a frame from.
    long lastFrame;

    FormsMonitorWindow(MonitorForm form) =>
        this.form = form;

    public static IMonitorWindow? Open(string title, int width, int height, WindowPlacement? placement, bool hidden, out string? error)
    {
        error = null;
        try
        {
            var form = new MonitorForm(title, width, height, placement);
            if (!hidden)
            {
                form.Show();
            }

            return new FormsMonitorWindow(form);
        }
        catch (Exception exception)
        {
            error = $"Could not create the window: {exception.Message}";
            return null;
        }
    }

    public bool Present(Screen screen)
    {
        if (form.IsDisposed)
        {
            return false;
        }

        form.Apply(screen);
        Application.DoEvents();
        return !form.IsDisposed;
    }

    /// <summary>
    /// Idles on the message queue, then handles whatever ended it before <see cref="Poll"/> reads
    /// it. No sooner than a frame after the last one ended, as when every frame slept for one: a
    /// wheel or a drag sends messages far faster than sixty a second, and each frame they woke
    /// would build the screen again for a change no one could see.
    /// </summary>
    public bool Wait(TimeSpan timeout, WaitHandle wake)
    {
        var since = Stopwatch.GetElapsedTime(lastFrame);
        if (since < MonitorProgram.Frame)
        {
            var rest = MonitorProgram.Frame - since;
            Thread.Sleep(rest);
            timeout -= rest;
        }

        MessageWait.For(wake, timeout);
        Application.DoEvents();
        lastFrame = Stopwatch.GetTimestamp();
        return true;
    }

    public MonitorInput Poll() =>
        form.Drain();

    public void SetHidden(bool hidden)
    {
        if (hidden)
        {
            form.Hide();
            return;
        }

        form.Show();
        if (form.WindowState == FormWindowState.Minimized)
        {
            form.WindowState = FormWindowState.Normal;
        }
    }

    public void Focus()
    {
        if (!form.Visible)
        {
            form.Show();
        }

        if (form.WindowState == FormWindowState.Minimized)
        {
            form.WindowState = FormWindowState.Normal;
        }

        form.Activate();
    }

    public bool SetClipboard(string text)
    {
        try
        {
            // SetText and not SetDataObject: the toolkit's own ten attempts over a second are most
            // of what a clipboard another process has open needs, and ClipboardPump has the rest.
            // SetText refuses empty text, and empty is how a triage clears the clipboard.
            if (text.Length == 0)
            {
                Clipboard.Clear();
                return true;
            }

            Clipboard.SetText(text);
            return true;
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Could not put {Length} characters on the clipboard", text.Length);
            return false;
        }
    }

    public string? PickDirectory(string? start)
    {
        try
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Choose your code directory",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };
            if (Directory.Exists(start))
            {
                dialog.SelectedPath = start;
            }

            if (dialog.ShowDialog(form) == DialogResult.OK)
            {
                return dialog.SelectedPath;
            }

            return null;
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Could not ask for a directory");
            return null;
        }
    }

    public bool Capture(Screen screen, int width, int height, string pngPath)
    {
        form.Apply(screen);
        form.ClientSize = new(width, height);
        using var bitmap = new Bitmap(width, height);
        form.DrawToBitmap(bitmap, new(0, 0, width, height));
        bitmap.Save(pngPath, ImageFormat.Png);
        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        form.AllowClose = true;
        form.Dispose();
    }
}
