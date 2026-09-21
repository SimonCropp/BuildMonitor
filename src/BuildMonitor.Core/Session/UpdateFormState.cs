sealed record UpdateFormState : FormState
{
    /// <summary>
    /// What the update page found running when it opened. Read once rather than rebuilt every
    /// frame, because listing processes is not something to do sixty times a second, and because
    /// the set the user was warned about should be the set they agreed to.
    /// </summary>
    public required McpServers Servers { get; init; }

    public override Page Page => Page.Update;
}
