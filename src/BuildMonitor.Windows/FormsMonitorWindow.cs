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

            return dialog.ShowDialog(form) == DialogResult.OK ? dialog.SelectedPath : null;
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
