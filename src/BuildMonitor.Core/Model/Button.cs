/// <summary>
/// One button along the bottom of the window.
/// </summary>
/// <param name="Tooltip">What it does, where the label does not say it, or empty for a button that
/// needs no explaining.</param>
record Button(string Label, bool Enabled, CommandKind Command, string Tooltip = "");
