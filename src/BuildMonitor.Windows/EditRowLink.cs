/// <summary>
/// A <see cref="FieldKind.EditRow"/>: the whole line is the click target that opens the row's
/// editor. A link rather than a flat button because this head draws a border around a button
/// whatever its FlatAppearance says, and a bordered row reads as a control that does something to
/// the row rather than one that opens it.
/// <para>
/// Its own type so <see cref="FormPanel"/> can tell it from a <see cref="FieldKind.Link"/>, which
/// shows only its label, and push the label and value into it as the health behind them changes.
/// </para>
/// </summary>
sealed class EditRowLink : LinkLabel
{
    public EditRowLink()
    {
        AutoSize = true;
        LinkColor = Palette.ChipText;
        ActiveLinkColor = Palette.Text;
    }

    /// <summary>
    /// Raises LinkClicked as a click on the row would. LinkLabel has no equivalent of
    /// <see cref="FormsButton.PerformClick"/>, so a test otherwise has to drive the mouse to cover
    /// the wiring that made the row clickable in the first place.
    /// </summary>
    public void PerformClick() =>
        OnLinkClicked(new(Links[0], MouseButtons.Left));
}
