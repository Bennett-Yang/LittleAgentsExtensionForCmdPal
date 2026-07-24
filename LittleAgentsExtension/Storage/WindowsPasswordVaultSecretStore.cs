using System.Runtime.InteropServices;
using Windows.Security.Credentials;

namespace LittleAgentsExtension.Storage;

internal sealed class WindowsPasswordVaultSecretStore : ISecretStore
{
    private const string Username = "apikey";
    private const uint ElementNotFoundHResult = 0x80070490;

    private readonly PasswordVault _passwordVault;

    public WindowsPasswordVaultSecretStore()
        : this(new PasswordVault())
    {
    }

    internal WindowsPasswordVaultSecretStore(PasswordVault passwordVault)
    {
        _passwordVault = passwordVault;
    }

    public void Set(string providerId, string apiKey)
    {
        // PasswordVault.Add replaces a credential with the same resource and user.
        // Avoid probing with Retrieve first: a missing credential throws, and that
        // negative lookup can block the Command Palette UI while saving a provider.
        _passwordVault.Add(new PasswordCredential(GetResourceName(providerId), Username, apiKey));
    }

    public string? TryGet(string providerId)
    {
        try
        {
            PasswordCredential credential = _passwordVault.Retrieve(GetResourceName(providerId), Username);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch (COMException exception) when ((uint)exception.HResult == ElementNotFoundHResult)
        {
            return null;
        }
    }

    public void Delete(string providerId)
    {
        try
        {
            PasswordCredential credential = _passwordVault.Retrieve(GetResourceName(providerId), Username);
            _passwordVault.Remove(credential);
        }
        catch (COMException exception) when ((uint)exception.HResult == ElementNotFoundHResult)
        {
        }
    }

    private static string GetResourceName(string providerId)
    {
        return $"LittleAgents.Provider.{providerId}";
    }
}
