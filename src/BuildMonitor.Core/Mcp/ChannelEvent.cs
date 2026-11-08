/// <summary>
/// One event pushed into a Claude Code session over its channel: the text, which becomes the body
/// of the <c>&lt;channel&gt;</c> tag the session reads, and the attributes the tag carries. Meta keys
/// are letters, digits and underscores only, since Claude Code drops any other without a word.
/// </summary>
record ChannelEvent(string Content, IReadOnlyDictionary<string, string> Meta);
