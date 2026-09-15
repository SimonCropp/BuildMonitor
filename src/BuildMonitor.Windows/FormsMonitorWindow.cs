/// <summary>
/// The WinForms <see cref="IMonitorWindow"/>. Pumped, not inverted: <see cref="MonitorProgram"/>
/// owns the loop, so a frame is one <see cref="Application.DoEvents"/> rather than
/// <see cref="Application.Run()"/>. That is what keeps the loop shared with the native heads.
/// DoEvents is safe here because nothing opens a modal dialog or nests a loop, and the state
/// is behind <see cref="SessionHost"/>.
/// </summary>
sealed class FormsMonitorWindow : IMonitorWindow
{
    MonitorForm form;
    bool disposed;

    FormsMonitorWindow(MonitorForm form) =>
        this.form = form;

    public static IMonitorWindow? Open(string title, int width, int height, bool hidden, out string? error)
    {
        error = null;
        try
        {
            var form = new MonitorForm(title, width, height);
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
        Thread.Sleep(16);
        return !form.IsDisposed;
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

    public void SetClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Clipboard");
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
