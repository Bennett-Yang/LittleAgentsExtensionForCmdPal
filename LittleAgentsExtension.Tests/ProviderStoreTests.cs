using LittleAgentsExtension.Storage;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace LittleAgentsExtension.Tests;

public sealed class ProviderStoreTests
{
    [Fact]
    public void Load_returns_empty_array_when_store_is_missing()
    {
        using TempStoreDirectory tempStore = new();

        ProviderStore store = new(tempStore.StorePath);

        Assert.Empty(store.Load());
    }

    [Fact]
    public void Save_then_Load_round_trips_provider()
    {
        using TempStoreDirectory tempStore = new();

        ProviderStore store = new(tempStore.StorePath);
        ProviderDef provider = CreateProvider("provider-a", "Provider A", "model-a");

        store.Save([provider]);

        ProviderDef[] loaded = store.Load();

        Assert.Single(loaded);
        Assert.Equal(provider, loaded[0]);
    }

    [Fact]
    public void Upsert_replaces_existing_provider_by_id()
    {
        using TempStoreDirectory tempStore = new();

        ProviderStore store = new(tempStore.StorePath);
        store.Save([CreateProvider("provider-a", "Provider A", "model-a")]);

        ProviderDef updated = CreateProvider("provider-a", "Provider A2", "model-b");

        store.Upsert(updated);

        ProviderDef[] loaded = store.Load();

        Assert.Single(loaded);
        Assert.Equal(updated, loaded[0]);
    }

    [Fact]
    public void Delete_removes_existing_provider()
    {
        using TempStoreDirectory tempStore = new();

        ProviderStore store = new(tempStore.StorePath);
        store.Save([
            CreateProvider("provider-a", "Provider A", "model-a"),
            CreateProvider("provider-b", "Provider B", "model-b")
        ]);

        store.Delete("provider-a");

        ProviderDef[] loaded = store.Load();

        Assert.Single(loaded);
        Assert.Equal(CreateProvider("provider-b", "Provider B", "model-b"), loaded[0]);
    }

    [Fact]
    public void Load_throws_InvalidDataException_for_corrupt_json()
    {
        using TempStoreDirectory tempStore = new();

        File.WriteAllText(tempStore.StorePath, "{");

        ProviderStore store = new(tempStore.StorePath);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => store.Load());

        Assert.IsType<JsonException>(exception.InnerException);
    }

    [Fact]
    public async Task Concurrent_saves_do_not_corrupt_file()
    {
        using TempStoreDirectory tempStore = new();

        ProviderStore store = new(tempStore.StorePath);
        string[] expectedIds = Enumerable.Range(0, 24).Select(index => $"provider-{index}").ToArray();

        await Task.WhenAll(
            expectedIds.Select((providerId, index) => Task.Run(() => store.Save([CreateProvider(providerId, $"Provider {index}", $"model-{index}")])))
        );

        ProviderDef[] loaded = store.Load();

        Assert.Single(loaded);
        Assert.Contains(loaded[0].Id, expectedIds);
    }

    private static ProviderDef CreateProvider(string id, string name, string defaultModel)
    {
        return new ProviderDef(id, name, "https://providers.example.test", defaultModel);
    }

    private sealed class TempStoreDirectory : IDisposable
    {
        private readonly string _rootPath;

        internal TempStoreDirectory()
        {
            _rootPath = Path.Combine(Path.GetTempPath(), $"little-agents-provider-store-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_rootPath);
        }

        internal string StorePath => Path.Combine(_rootPath, "providers.json");

        public void Dispose()
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }
    }
}
