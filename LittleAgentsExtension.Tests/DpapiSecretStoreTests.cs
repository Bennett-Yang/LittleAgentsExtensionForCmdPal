using LittleAgentsExtension.Common;
using LittleAgentsExtension.Storage;
using Xunit;

namespace LittleAgentsExtension.Tests;

public sealed class DpapiSecretStoreTests
{
    [WindowsOnlyFact]
    public void Set_then_TryGet_returns_value_for_same_provider()
    {
        string providerId = CreateProviderId();
        DpapiSecretStore store = new();

        try
        {
            store.Set(providerId, "value-a");

            Assert.Equal("value-a", store.TryGet(providerId));
        }
        finally
        {
            store.Delete(providerId);
        }
    }

    [WindowsOnlyFact]
    public void Delete_then_TryGet_returns_null()
    {
        string providerId = CreateProviderId();
        DpapiSecretStore store = new();

        store.Set(providerId, "value-a");
        store.Delete(providerId);

        Assert.Null(store.TryGet(providerId));
    }

    [WindowsOnlyFact]
    public void TryGet_returns_null_when_secret_file_is_tampered()
    {
        string providerId = CreateProviderId();
        DpapiSecretStore store = new();

        try
        {
            store.Set(providerId, "value-a");
            string secretPath = DpapiSecretStore.GetSecretPath(providerId);
            byte[] protectedBytes = File.ReadAllBytes(secretPath);
            protectedBytes[0] ^= 0xFF;
            File.WriteAllBytes(secretPath, protectedBytes);

            Assert.Null(store.TryGet(providerId));
        }
        finally
        {
            store.Delete(providerId);
        }
    }

    [Fact]
    public void GetSecretPath_keeps_malformed_provider_id_inside_secrets_directory()
    {
        string providerId = $"..{Path.DirectorySeparatorChar}outside{Path.AltDirectorySeparatorChar}provider:bad";
        string secretsDir = Path.GetFullPath(Path.Combine(PathHelper.LocalStateDir, "secrets"));

        string secretPath = Path.GetFullPath(DpapiSecretStore.GetSecretPath(providerId));

        Assert.StartsWith(secretsDir + Path.DirectorySeparatorChar, secretPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(".bin", Path.GetExtension(secretPath));
        Assert.DoesNotContain("outside", Path.GetFileName(secretPath), StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateProviderId()
    {
        return $"dpapi-test-{Guid.NewGuid():N}";
    }

    private sealed class WindowsOnlyFactAttribute : FactAttribute
    {
        public WindowsOnlyFactAttribute()
        {
            if (!OperatingSystem.IsWindows())
            {
                Skip = "Requires Windows DPAPI.";
            }
        }
    }
}
