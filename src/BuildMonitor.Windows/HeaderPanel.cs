/// <summary>
/// The header: what the page is about on the left and, on the builds page, the filter box on the
/// right. The box is pushed the session's text only while it does not have the focus, so a frame
/// never fights the typist, and it reports what was typed but not what was pushed, or every frame
/// would echo the text back as an edit.
/// <para>
/// The box is a bordered panel holding a borderless text box and the cross that empties it, rather
/// than a bordered text box: a native text box has no room of its own to put anything in, and a
/// cross drawn over one would sit on top of the text it is there to clear.
/// </para>
/// </summary>
sealed class HeaderPanel : Panel
{
    const int gap = 10;
    const int boxWidth = 240;

    /// <summary>
    /// The cross's cell, wide enough to aim at without taking the room the text needs.
    /// </summary>
    const int clearWidth = 20;

    const int boxPadding = 4;
    LiveLabel text;
    Panel box;
    TextBox search;
    FormsButton clear;
    bool searchShown = true;
    bool applying;
    bool searchChanged;

    public HeaderPanel()
    {
        Height = LogicalToDeviceUnits(40);
        BackColor = Palette.Surface;
        text = new()
        {
            Padding = DpiScale.Spacing(this, gap, 0, gap, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Palette.Dim,
            BackColor = Palette.Surface,
            AutoEllipsis = true,
            UseMnemonic = false
        };
        box = new()
        {
            Width = LogicalToDeviceUnits(boxWidth),
            BackColor = Palette.Background,
            BorderStyle = BorderStyle.FixedSingle
        };
        search = new()
        {
            BackColor = Palette.Background,
            ForeColor = Palette.Text,
            BorderStyle = BorderStyle.None,
            PlaceholderText = "Filter"
        };
        clear = new()
        {
            // The multiplication sign rather than a heavier cross: it is in every face a head can
            // be running, where the crosses drawn for this are not.
            Text = "×",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Palette.Dim,
            BackColor = Palette.Background,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            UseMnemonic = false,
            // Out of the tab order and never the focus it is given by being pressed: the box it
            // empties is where whoever pressed it is typing.
            TabStop = false,
            Visible = false
        };
        // As the filters page draws the cross that removes a filter: flat, no border of its own,
        // and no highlight behind it, so it reads as a mark inside the box rather than a button
        // beside the text.
        clear.FlatAppearance.BorderSize = 0;
        clear.FlatAppearance.MouseOverBackColor = Palette.Background;
        clear.FlatAppearance.MouseDownBackColor = Palette.Background;
        // Emptying the box is an edit like any other, so the session hears it through the same
        // drain that typing goes through.
        clear.Click += (_, _) =>
        {
            search.Clear();
            search.Focus();
        };
        clear.MouseEnter += (_, _) => clear.ForeColor = Palette.Text;
        clear.MouseLeave += (_, _) => clear.ForeColor = Palette.Dim;
        search.TextChanged += (_, _) =>
        {
            clear.Visible = search.Text.Length > 0;
            if (!applying)
            {
                searchChanged = true;
            }
        };
        box.Controls.Add(search);
        box.Controls.Add(clear);
        Controls.Add(text);
        Controls.Add(box);
        // Subscribed only now: the base constructor and the height set above resize the panel before
        // the box exists. The box's own size changes too, since a single line box takes the height of
        // its font, which it only learns once parented.
        Resize += (_, _) => Arrange();
        search.SizeChanged += (_, _) => Arrange();
        Arrange();
    }

    public bool SearchFocused => search.Focused;

    /// <param name="summary">The builds page's counts, or a form's title.</param>
    /// <param name="filter">The filter box's text, or null for a page without the box.</param>
    public void Apply(string summary, string? filter)
    {
        text.Text = summary;
        var shown = filter is not null;
        if (shown != searchShown)
        {
            searchShown = shown;
            box.Visible = shown;
            Arrange();
        }

        if (filter is not null &&
            !search.Focused &&
            search.Text != filter)
        {
            applying = true;
            search.Text = filter;
            applying = false;
        }
    }

    public void FocusSearch()
    {
        if (!searchShown)
        {
            return;
        }

        search.Focus();
        search.SelectAll();
    }

    /// <summary>
    /// Empties the box, reporting it as an edit. False when it was already empty, so Escape falls
    /// through to hiding the window only once there is no filter left to clear.
    /// </summary>
    public bool ClearSearch()
    {
        if (search.Text.Length == 0)
        {
            return false;
        }

        search.Clear();
        return true;
    }

    public string? DrainSearch()
    {
        if (!searchChanged)
        {
            return null;
        }

        searchChanged = false;
        return search.Text;
    }

    public void Retheme()
    {
        BackColor = Palette.Surface;
        text.ForeColor = Palette.Dim;
        text.BackColor = Palette.Surface;
        box.BackColor = Palette.Background;
        search.BackColor = Palette.Background;
        search.ForeColor = Palette.Text;
        clear.BackColor = Palette.Background;
        clear.ForeColor = Palette.Dim;
    }

    /// <summary>
    /// Placed by hand rather than docked: a docked single line box keeps the height of its font and
    /// sits at the top of the header rather than centred in it.
    /// </summary>
    void Arrange()
    {
        var right = Width;
        if (searchShown)
        {
            var padding = LogicalToDeviceUnits(boxPadding);
            box.Height = search.Height + 2 * padding;
            box.Location = new(Width - box.Width - LogicalToDeviceUnits(gap), (Height - box.Height) / 2);
            var cross = LogicalToDeviceUnits(clearWidth);
            search.Bounds = new(padding, padding, Math.Max(0, box.ClientSize.Width - padding - cross), search.Height);
            clear.Bounds = new(box.ClientSize.Width - cross, 0, cross, box.ClientSize.Height);
            right = box.Left;
        }

        text.Bounds = new(0, 0, Math.Max(0, right), Height);
    }
}
