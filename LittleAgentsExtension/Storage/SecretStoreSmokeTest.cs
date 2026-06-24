using LittleAgentsExtension.Common;
using System;
using System.Globalization;
using System.IO;
using Windows.Security.Credentials;

namespace LittleAgentsExtension.Storage;

internal static class SecretStoreSmokeTest
{
    private const string Resource = "LittleAgents.Smoke";
    private const string Username = "u";
    private const string Password = "p";

    public static void Run()
    {
        string logPath = Path.Combine(PathHelper.LocalStateDir, "spike-vault-mta.log");
        string result;
        PasswordVault? vault = null;

        try
        {
            vault = new PasswordVault();
            PasswordCredential credential = new(Resource, Username, Password);
            vault.Add(credential);
            PasswordCredential retrieved = vault.Retrieve(Resource, Username);
            retrieved.RetrievePassword();
            result = retrieved.Password == Password ? "OK" : "FAILED: password mismatch";
        }
        catch (Exception exception)
        {
            result = $"FAILED: {Sanitize(exception)}";
        }
        finally
        {
            TryRemove(vault);
        }

        File.WriteAllText(logPath, result);
        Console.WriteLine($"Vault smoke result written to {logPath}: {result}");
    }

    private static void TryRemove(PasswordVault? vault)
    {
        if (vault is null)
        {
            return;
        }

        try
        {
            vault.Remove(vault.Retrieve(Resource, Username));
        }
        catch (Exception)
        {
        }
    }

    private static string Sanitize(Exception exception)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{exception.GetType().FullName} HResult=0x{exception.HResult:X8}");
    }
}
