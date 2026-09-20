/// <summary>
/// What one part of a row says on hover. Composed here rather than in a head, like every other
/// string on <see cref="BuildRow"/>, so three heads cannot describe the same link three ways.
/// </summary>
record RowTooltip(RowPart Part, string Text);
