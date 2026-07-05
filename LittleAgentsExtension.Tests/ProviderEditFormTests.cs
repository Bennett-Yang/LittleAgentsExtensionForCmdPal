using LittleAgentsExtension.Storage;
using Xunit;

namespace LittleAgentsExtension.Tests;

public sealed class ProviderEditFormTests
{
    [Fact]
    public void SubmitForm_creates_provider_and_stores_api_key_separately()
    {
        using TempProviderStore tempStore = new();
        ProviderStore providers = new(tempStore.StorePath);
        SecretStoreContractTests.InMemorySecretStore secrets = new();
        ProviderEditForm form = new(providers, secrets, existing: null);

        form.SubmitForm(Payload("Provider A", "https://api.openai.com/v1", "sk-test-form-create", "gpt-4.1-mini"));

        ProviderDef provider = Assert.Single(providers.Load());
        Assert.Equal("Provider A", provider.Name);
        Assert.Equal("https://api.openai.com/v1", provider.BaseUrl);
        Assert.Equal("gpt-4.1-mini", provider.DefaultModel);
        Assert.Equal("sk-test-form-create", secrets.TryGet(provider.Id));
        Assert.DoesNotContain("sk-test-form-create", File.ReadAllText(tempStore.StorePath), StringComparison.Ordinal);
    }

    [Fact]
    public void SubmitForm_requires_api_key_when_creating_provider()
    {
        using TempProviderStore tempStore = new();
        ProviderStore providers = new(tempStore.StorePath);
        SecretStoreContractTests.InMemorySecretStore secrets = new();
        ProviderEditForm form = new(providers, secrets, existing: null);

        form.SubmitForm(Payload("Provider A", "https://api.openai.com/v1", string.Empty, "gpt-4.1-mini"));

        Assert.Empty(providers.Load());
    }

    [Fact]
    public void SubmitForm_rejects_non_http_base_url()
    {
        using TempProviderStore tempStore = new();
        ProviderStore providers = new(tempStore.StorePath);
        SecretStoreContractTests.InMemorySecretStore secrets = new();
        ProviderEditForm form = new(providers, secrets, existing: null);

        form.SubmitForm(Payload("Provider A", "file:///tmp/provider", "sk-test-form-invalid", "gpt-4.1-mini"));

        Assert.Empty(providers.Load());
        Assert.Null(secrets.TryGet("provider-a"));
    }

    [Fact]
    public void SubmitForm_keeps_existing_secret_when_edit_api_key_is_empty()
    {
        using TempProviderStore tempStore = new();
        ProviderStore providers = new(tempStore.StorePath);
        SecretStoreContractTests.InMemorySecretStore secrets = new();
        ProviderDef existing = new("provider-a", "Provider A", "https://api.openai.com/v1", "gpt-4.1-mini");
        providers.Save([existing]);
        secrets.Set(existing.Id, "sk-test-original");
        ProviderEditForm form = new(providers, secrets, existing);

        form.SubmitForm(Payload("Provider A2", "https://openrouter.ai/api/v1", string.Empty, string.Empty));

        ProviderDef provider = Assert.Single(providers.Load());
        Assert.Equal(existing.Id, provider.Id);
        Assert.Equal("Provider A2", provider.Name);
        Assert.Equal("https://openrouter.ai/api/v1", provider.BaseUrl);
        Assert.Null(provider.DefaultModel);
        Assert.Equal("sk-test-original", secrets.TryGet(existing.Id));
    }

    [Fact]
    public void SubmitForm_replaces_existing_secret_when_edit_api_key_is_present()
    {
        using TempProviderStore tempStore = new();
        ProviderStore providers = new(tempStore.StorePath);
        SecretStoreContractTests.InMemorySecretStore secrets = new();
        ProviderDef existing = new("provider-a", "Provider A", "https://api.openai.com/v1", null);
        providers.Save([existing]);
        secrets.Set(existing.Id, "sk-test-original");
        ProviderEditForm form = new(providers, secrets, existing);

        form.SubmitForm(Payload("Provider A", "http://localhost:11434/v1", "sk-test-replacement", "llama3.1"));

        ProviderDef provider = Assert.Single(providers.Load());
        Assert.Equal(existing.Id, provider.Id);
        Assert.Equal("http://localhost:11434/v1", provider.BaseUrl);
        Assert.Equal("llama3.1", provider.DefaultModel);
        Assert.Equal("sk-test-replacement", secrets.TryGet(existing.Id));
        Assert.DoesNotContain("sk-test-replacement", File.ReadAllText(tempStore.StorePath), StringComparison.Ordinal);
    }

    private static string Payload(string name, string baseUrl, string apiKey, string defaultModel)
    {
        return $$"""
        {
            "Name": {{Json(name)}},
            "BaseUrl": {{Json(baseUrl)}},
            "ApiKey": {{Json(apiKey)}},
            "DefaultModel": {{Json(defaultModel)}}
        }
        """;
    }

    private static string Json(string value)
    {
        return System.Text.Json.JsonSerializer.Serialize(value);
    }

    private sealed class TempProviderStore : IDisposable
    {
        private readonly string _rootPath;

        internal TempProviderStore()
        {
            _rootPath = Path.Combine(Path.GetTempPath(), $"little-agents-provider-form-{Guid.NewGuid():N}");
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
