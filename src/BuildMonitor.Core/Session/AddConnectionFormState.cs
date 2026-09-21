/// <summary>
/// A connection that does not exist yet. Its id is a draft, assigned when the page opened, so
/// abandoning the page has to delete whatever a sign in stored under it.
/// </summary>
sealed record AddConnectionFormState : ConnectionFormState
{
    public override Page Page => Page.AddConnection;

    /// <summary>
    /// Whatever the provider drop down says, which is the one thing on this page that changes
    /// which other fields there are.
    /// </summary>
    public override ProviderDescriptor Descriptor =>
        ProviderDescriptors.ByName(Value(FormFields.Provider)) ?? ProviderDescriptors.All[0];
}
