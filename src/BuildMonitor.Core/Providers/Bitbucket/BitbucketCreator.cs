class BitbucketCreator
{
    /// <summary>
    /// The account id, a guid in braces. Read only to leave the name behind for whoever else is
    /// handed that id and no name; see <see cref="IdentityNames"/>.
    /// </summary>
    public string? Uuid { get; set; }

    public string? DisplayName { get; set; }
}