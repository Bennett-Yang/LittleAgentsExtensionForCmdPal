using LittleAgentsExtension.Storage;
using Xunit;

namespace LittleAgentsExtension.Tests;

public sealed class SecretStoreContractTests
{
    [Fact]
    public void Set_then_TryGet_returns_value_for_same_provider()
    {
        InMemorySecretStore store = new();

        store.Set("provider-a", "value-a");

        Assert.Equal("value-a", store.TryGet("provider-a"));
    }

    [Fact]
    public void Delete_then_TryGet_returns_null()
    {
        InMemorySecretStore store = new();

        store.Set("provider-a", "value-a");
        store.Delete("provider-a");

        Assert.Null(store.TryGet("provider-a"));
    }

    [Fact]
    public void Multiple_providers_do_not_collide()
    {
        InMemorySecretStore store = new();

        store.Set("provider-a", "value-a");
        store.Set("provider-b", "value-b");

        Assert.Equal("value-a", store.TryGet("provider-a"));
        Assert.Equal("value-b", store.TryGet("provider-b"));
    }

    [Fact]
    public void Set_twice_for_same_provider_overwrites_previous_value()
    {
        InMemorySecretStore store = new();

        store.Set("provider-a", "value-a");
        store.Set("provider-a", "value-b");

        Assert.Equal("value-b", store.TryGet("provider-a"));
    }

    internal sealed class InMemorySecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

        public void Set(string providerId, string apiKey)
        {
            _secrets[providerId] = apiKey;
        }

        public string? TryGet(string providerId)
        {
            return _secrets.TryGetValue(providerId, out string? apiKey) ? apiKey : null;
        }

        public void Delete(string providerId)
        {
            _secrets.Remove(providerId);
        }
    }
}
