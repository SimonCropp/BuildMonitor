/// <summary>
/// Where tokens live. One implementation per platform, chosen by <see cref="SecretStores"/>;
/// settings.json never sees a value.
/// </summary>
interface ISecretStore
{
    string? Read(string key);

    void Write(string key, string value);

    void Delete(string key);
}
