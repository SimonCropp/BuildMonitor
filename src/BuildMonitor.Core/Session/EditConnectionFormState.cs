/// <summary>
/// A connection that already exists. Its provider is part of the form's shape rather than one of
/// its values: the stored credential, the scopes and the builds all belong to that service, so a
/// field that could change it would describe a different connection under the same id, and a
/// provider change re-derives the server, the sign in method and the scopes as it does so.
/// </summary>
sealed record EditConnectionFormState : ConnectionFormState
{
    public required string ProviderId { get; init; }

    public override Page Page => Page.EditConnection;

    public override ProviderDescriptor Descriptor =>
        ProviderDescriptors.Get(ProviderId);
}
