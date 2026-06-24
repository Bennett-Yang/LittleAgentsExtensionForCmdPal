using System;
using Windows.Security.Credentials;

namespace LittleAgentsExtension.Storage;

internal static class SecretStoreFactory
{
    private static readonly Lazy<ISecretStore> CachedStore = new(CreateUncached);

    public static ISecretStore Create()
    {
        return CachedStore.Value;
    }

    private static ISecretStore CreateUncached()
    {
        return CanUsePasswordVault() ? new WindowsPasswordVaultSecretStore() : new DpapiSecretStore();
    }

    private static bool CanUsePasswordVault()
    {
        const string resource = "LittleAgents.FactorySmoke";
        const string username = "u";
        const string password = "p";

        PasswordVault vault = new();
        PasswordCredential credential = new(resource, username, password);

        try
        {
            vault.Add(credential);
            PasswordCredential retrieved = vault.Retrieve(resource, username);
            retrieved.RetrievePassword();
            return retrieved.Password == password;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            TryRemove(vault, resource, username);
        }
    }

    private static void TryRemove(PasswordVault vault, string resource, string username)
    {
        try
        {
            vault.Remove(vault.Retrieve(resource, username));
        }
        catch (Exception)
        {
        }
    }
}
