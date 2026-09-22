using System.Diagnostics.CodeAnalysis;

/// <summary>
/// A label for text that changes while it is watched. Setting a stock label's text has the native
/// control redraw itself there and then, outside the buffered paint, and it shows empty until that
/// paint lands: about one frame in twenty caught the footer blank while a first poll counted its
/// status line up dozens of times a second, so the whole line flickered. Buffering the label does
/// not help, as the blank is not drawn in a paint. So redraw is off while the text is set, and the
/// buffered paint draws the new text over the old.
/// <para>
/// Only while the label is visible: turning redraw back on shows the window, so doing it to a label
/// that was hidden would put it back on screen.
/// </para>
/// </summary>
sealed class LiveLabel : FormsLabel
{
    const int setRedraw = 0x000B;

    [AllowNull]
    public override string Text
    {
        get => base.Text;
        set
        {
            if (!IsHandleCreated ||
                !Visible ||
                (value ?? "") == base.Text)
            {
                base.Text = value;
                return;
            }

            Redraw(false);
            try
            {
                base.Text = value;
            }
            finally
            {
                Redraw(true);
            }

            Invalidate();
        }
    }

    void Redraw(bool enabled)
    {
        var message = System.Windows.Forms.Message.Create(Handle, setRedraw, enabled ? 1 : 0, 0);
        DefWndProc(ref message);
    }
}
