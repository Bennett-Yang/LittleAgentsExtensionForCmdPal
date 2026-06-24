namespace LittleAgentsExtension.Storage;

internal interface ISecretStore
{
    void Set(string providerId, string apiKey);

    string? TryGet(string providerId);

    void Delete(string providerId);
}
