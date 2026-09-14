/// <summary>
/// A form page as real controls, one per <see cref="Field"/>, keyed by id. Rebuilt only when
/// the sequence of ids changes; otherwise values are pushed into controls that do not have the
/// focus, so a frame never fights the typist.
/// </summary>
sealed class FormPanel : Panel
{
    readonly TableLayoutPanel table;
    readonly Dictionary<string, Control> controls = new();
    readonly List<FieldChange> changes = [];
    string? clickedField;
    string signature = "";
    bool applying;

    public FormPanel()
    {
        BackColor = Palette.Background;
        AutoScroll = true;
        Padding = new(12);
        table = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            BackColor = Palette.Background
        };
        table.ColumnStyles.Add(new(SizeType.AutoSize));
        table.ColumnStyles.Add(new(SizeType.Percent, 100));
        Controls.Add(table);
    }

    public void Apply(FormPage form)
    {
        var next = string.Join('|', form.Fields.Select(_ => $"{_.Id}:{_.Kind}"));
        applying = true;
        try
        {
            if (next != signature)
            {
                signature = next;
                Rebuild(form);
            }

            foreach (var field in form.Fields)
            {
                if (controls.TryGetValue(field.Id, out var control))
                {
                    Update(control, field);
                }
            }
        }
        finally
        {
            applying = false;
        }
    }

    /// <summary>
    /// Every field control holds its colours, so the next <see cref="Apply"/> rebuilds them all.
    /// </summary>
    public void Retheme()
    {
        BackColor = Palette.Background;
        table.BackColor = Palette.Background;
        signature = "";
    }

    void Rebuild(FormPage form)
    {
        table.SuspendLayout();
        // A snapshot: disposing a control removes it from table.Controls.
        foreach (var control in table.Controls.Cast<Control>().ToList())
        {
            control.Dispose();
        }

        table.Controls.Clear();
        table.RowStyles.Clear();
        table.RowCount = 0;
        controls.Clear();
        var row = 0;
        foreach (var field in form.Fields)
        {
            var (label, control) = Create(field);
            table.RowStyles.Add(new(SizeType.AutoSize));
            if (label is null)
            {
                table.Controls.Add(control, 0, row);
                table.SetColumnSpan(control, 2);
            }
            else
            {
                table.Controls.Add(label, 0, row);
                table.Controls.Add(control, 1, row);
            }

            controls[field.Id] = control;
            row++;
        }

        table.RowCount = row;
        table.ResumeLayout();
    }

    (Control? Label, Control Control) Create(Field field)
    {
        switch (field.Kind)
        {
            case FieldKind.Checkbox:
            {
                var box = new CheckBox { Text = field.Label, AutoSize = true, ForeColor = Palette.Text, Margin = new(3, 6, 3, 6) };
                box.CheckedChanged += (_, _) => Changed(field.Id, box.Checked ? "true" : "false");
                return (null, box);
            }
            case FieldKind.Text:
            case FieldKind.Password:
            case FieldKind.Number:
            {
                var text = new TextBox
                {
                    Width = field.Kind == FieldKind.Number ? 90 : 420,
                    BackColor = Palette.Surface,
                    ForeColor = Palette.Text,
                    BorderStyle = BorderStyle.FixedSingle,
                    UseSystemPasswordChar = field.Kind == FieldKind.Password,
                    PlaceholderText = field.Hint ?? "",
                    Margin = new(3, 4, 3, 4)
                };
                text.TextChanged += (_, _) => Changed(field.Id, text.Text);
                return (Label(field.Label), text);
            }
            case FieldKind.Select:
            {
                var select = new SelectControl
                {
                    Options = field.Options ?? [],
                    Margin = new(3, 4, 3, 4)
                };
                select.ValueChanged += (_, _) => Changed(field.Id, select.Value);
                return (Label(field.Label), select);
            }
            case FieldKind.Button:
            {
                var button = new FormsButton
                {
                    Text = field.Label,
                    AutoSize = true,
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = Palette.Text,
                    BackColor = Palette.Chip,
                    Margin = new(3, 8, 3, 8)
                };
                button.FlatAppearance.BorderColor = Palette.Border;
                button.Click += (_, _) => clickedField = field.Id;
                return (null, button);
            }
            case FieldKind.Link:
            {
                var link = new LinkLabel
                {
                    Text = field.Label,
                    AutoSize = true,
                    LinkColor = Palette.ChipText,
                    ActiveLinkColor = Palette.Text,
                    Margin = new(3, 6, 3, 6)
                };
                link.LinkClicked += (_, _) => clickedField = field.Id;
                return (null, link);
            }
            case FieldKind.ListRow:
            {
                var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new(3, 2, 3, 2), BackColor = Palette.Background };
                var text = new FormsLabel { AutoSize = true, ForeColor = Palette.Text, Margin = new(0, 6, 8, 0) };
                var remove = new FormsButton
                {
                    Text = "✕",
                    AutoSize = false,
                    Size = new(24, 22),
                    Margin = new(0, 2, 0, 0),
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = Palette.Dim,
                    BackColor = Palette.Background,
                    Tag = "remove"
                };
                remove.FlatAppearance.BorderSize = 0;
                remove.Click += (_, _) => clickedField = field.Id;
                panel.Controls.Add(text);
                panel.Controls.Add(remove);
                return (null, panel);
            }
            default:
            {
                var label = new FormsLabel
                {
                    AutoSize = true,
                    MaximumSize = new(900, 0),
                    ForeColor = field.Id == "error" ? Palette.Error : Palette.Text,
                    Margin = new(3, 6, 3, 6)
                };
                return (null, label);
            }
        }
    }

    static FormsLabel Label(string text) =>
        new()
        {
            Text = text,
            AutoSize = true,
            ForeColor = Palette.Dim,
            Margin = new(3, 8, 12, 3),
            Anchor = AnchorStyles.Left
        };

    static void Update(Control control, Field field)
    {
        control.Enabled = field.Enabled || control is FormsLabel or LinkLabel;
        switch (control)
        {
            case CheckBox box:
                if (!box.Focused)
                {
                    box.Checked = field.Value == "true";
                }

                break;
            case TextBox text:
                if (!text.Focused &&
                    text.Text != field.Value)
                {
                    text.Text = field.Value;
                }

                break;
            case SelectControl select:
                select.Value = field.Value;
                break;
            case FlowLayoutPanel panel when panel.Controls[0] is FormsLabel text:
                text.Text = field.Label.Length == 0 ? field.Value : $"{field.Label}: {field.Value}";
                break;
            case LinkLabel link:
                link.Text = field.Label;
                break;
            case FormsLabel label:
                label.Text = field.Label.Length == 0 ? field.Value : $"{field.Label}: {field.Value}";
                break;
        }
    }

    void Changed(string id, string value)
    {
        if (!applying)
        {
            changes.Add(new(id, value));
        }
    }

    public IReadOnlyList<FieldChange>? DrainChanges()
    {
        if (changes.Count == 0)
        {
            return null;
        }

        var drained = changes.ToList();
        changes.Clear();
        return drained;
    }

    public string? DrainClickedField()
    {
        var field = clickedField;
        clickedField = null;
        return field;
    }
}
