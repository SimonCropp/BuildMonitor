/// <summary>
/// One button on a build's row. The label is composed on the managed side, pull request number and
/// all, so no head spells one differently.
/// </summary>
record RowChip(ChipKind Kind, string Label);
