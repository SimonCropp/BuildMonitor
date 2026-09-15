/// <summary>
/// The header: what the page is about on the left and, on the builds page, the filter box on the
/// right. The box is pushed the session's text only while it does not have the focus, so a frame
/// never fights the typist, and it reports what was typed but not what was pushed, or every frame
/// would echo the text back as an edit.
/// </summary>
sealed class HeaderPanel : Panel
{
    const int gap = 10;
    const int boxWidth = 240;
    readonly FormsLabel text;
    readonly TextBox search;
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
        search = new()
        {
            Width = LogicalToDeviceUnits(boxWidth),
            BackColor = Palette.Background,
            ForeColor = Palette.Text,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "Filter"
        };
        search.TextChanged += (_, _) =>
        {
            if (!applying)
            {
                searchChanged = true;
            }
        };
        Controls.Add(text);
        Controls.Add(search);
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
            search.Visible = shown;
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
        search.BackColor = Palette.Background;
        search.ForeColor = Palette.Text;
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
            search.Location = new(Width - search.Width - LogicalToDeviceUnits(gap), (Height - search.Height) / 2);
            right = search.Left;
        }

        text.Bounds = new(0, 0, Math.Max(0, right), Height);
    }
}
