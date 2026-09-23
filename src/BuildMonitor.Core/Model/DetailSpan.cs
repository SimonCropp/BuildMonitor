/// <summary>
/// One run of a build row's second cell: plain text, or a link a click opens. The cell is split on
/// the managed side, so no head decides for itself which word is the branch, and a pipeline named
/// like a branch cannot send a click to the wrong page.
/// </summary>
/// <param name="Link">What a click on the run opens, as the kind a head reports for it, or
/// <see cref="ChipKind.None"/> for text that opens nothing.</param>
/// <param name="Icon">A row icon drawn before the text and part of the same run, so a click on it
/// is a click on the text; or empty. The branch carries <see cref="BranchIcon"/>: between two names
/// in one colour, a space was all that marked where the pipeline ended, and either name can have
/// spaces of its own.</param>
record DetailSpan(string Text, ChipKind Link = ChipKind.None, string Icon = "")
{
    public const string BranchIcon = "branch";

    /// <summary>
    /// What stands in for <see cref="BranchIcon"/> where only text can go, the text head and the
    /// hovers, written before the branch as the picture is drawn: a ref, as in
    /// actions/checkout@main. A hover that joined the two with a space said no more than the row
    /// did before the mark.
    /// </summary>
    public const string BranchIconText = "@";
}
