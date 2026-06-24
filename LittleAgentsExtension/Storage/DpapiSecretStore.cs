using LittleAgentsExtension.Common;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LittleAgentsExtension.Storage;

internal sealed class DpapiSecretStore : ISecretStore
{
    private static string SecretsDir => Path.Combine(PathHelper.LocalStateDir, "secrets");

    public void Set(string providerId, string apiKey)
    {
        Directory.CreateDirectory(SecretsDir);
        byte[] plaintext = Encoding.UTF8.GetBytes(apiKey);
        byte[] protectedBytes = ProtectedData.Protect(plaintext, GetEntropy(providerId), DataProtectionScope.CurrentUser);
        string secretPath = GetSecretPath(providerId);
        string tempPath = Path.Combine(Path.GetDirectoryName(secretPath)!, Path.GetRandomFileName());

        File.WriteAllBytes(tempPath, protectedBytes);
        File.Move(tempPath, secretPath, overwrite: true);
    }

    public string? TryGet(string providerId)
    {
        string secretPath = GetSecretPath(providerId);
        if (!File.Exists(secretPath))
        {
            return null;
        }

        try
        {
            byte[] protectedBytes = File.ReadAllBytes(secretPath);
            byte[] plaintext = ProtectedData.Unprotect(protectedBytes, GetEntropy(providerId), DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public void Delete(string providerId)
    {
        File.Delete(GetSecretPath(providerId));
    }

    internal static string GetSecretPath(string providerId)
    {
        string fileName = Convert.ToHexString(SHA256.HashData(GetEntropy(providerId)));
        return Path.Combine(SecretsDir, $"{fileName}.bin");
    }

    private static byte[] GetEntropy(string providerId)
    {
        return Encoding.UTF8.GetBytes(providerId);
    }
}
