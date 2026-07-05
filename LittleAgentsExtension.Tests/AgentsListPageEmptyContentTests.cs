using System.Reflection;
using LittleAgentsExtension.Llm;
using LittleAgentsExtension.Storage;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Xunit;

namespace LittleAgentsExtension.Tests;

public sealed class AgentsListPageEmptyContentTests
{
    [Fact]
    public void Toolkit_empty_content_property_is_settable_command_item()
    {
        PropertyInfo property = typeof(DynamicListPage).GetProperty("EmptyContent")
            ?? throw new InvalidOperationException("DynamicListPage.EmptyContent is missing.");

        Assert.Equal(typeof(ICommandItem), property.PropertyType);
        Assert.True(property.CanWrite);
    }

    [Fact]
    public void Empty_store_points_to_provider_onboarding()
    {
        using TempStoreDirectory tempStore = new();
        AgentsListPage page = CreatePage(tempStore.CreateAgentStore(), tempStore.CreateProviderStore());

        ICommandItem emptyContent = GetRequiredEmptyContent(page);
        ProviderEditFormPage targetPage = GetCommand<ProviderEditFormPage>(emptyContent);

        Assert.Equal("Add a provider first", GetProperty<string>(emptyContent, "Title"));
        Assert.Equal("You'll need an OpenAI-compatible endpoint and API key", GetProperty<string>(emptyContent, "Subtitle"));
        Assert.Equal("little-agents.provider.new", targetPage.Id);
    }

    [Fact]
    public void Provider_only_store_points_to_agent_onboarding()
    {
        using TempStoreDirectory tempStore = new();
        AgentStore agents = tempStore.CreateAgentStore();
        ProviderStore providers = tempStore.CreateProviderStore();
        providers.Save([CreateProvider("provider-a")]);
        AgentsListPage page = CreatePage(agents, providers);

        ICommandItem emptyContent = GetRequiredEmptyContent(page);
        AgentEditFormPage targetPage = GetCommand<AgentEditFormPage>(emptyContent);

        Assert.Equal("Create your first agent", GetProperty<string>(emptyContent, "Title"));
        Assert.Equal("Define a system prompt and pick a provider", GetProperty<string>(emptyContent, "Subtitle"));
        Assert.Equal("little-agents.agent.new", targetPage.Id);
    }

    [Fact]
    public void Filtered_empty_search_does_not_set_empty_content_when_agents_exist()
    {
        using TempStoreDirectory tempStore = new();
        AgentStore agents = tempStore.CreateAgentStore();
        ProviderStore providers = tempStore.CreateProviderStore();
        agents.Save([CreateAgent("agent-a", "provider-a")]);
        providers.Save([CreateProvider("provider-a")]);
        AgentsListPage page = CreatePage(agents, providers);

        page.UpdateSearchText(string.Empty, "no matching agent");
        page.GetItems();

        Assert.Null(page.EmptyContent);
    }

    [Fact]
    public void Store_changes_refresh_empty_content_from_cached_store_state()
    {
        using TempStoreDirectory tempStore = new();
        AgentStore agents = tempStore.CreateAgentStore();
        ProviderStore providers = tempStore.CreateProviderStore();
        AgentsListPage page = CreatePage(agents, providers);

        providers.Upsert(CreateProvider("provider-a"));
        agents.Upsert(CreateAgent("agent-a", "provider-a"));

        Assert.Null(page.EmptyContent);
    }

    private static AgentsListPage CreatePage(AgentStore agents, ProviderStore providers)
    {
        return new AgentsListPage(agents, providers, new SecretStoreContractTests.InMemorySecretStore(), new EmptyLlmChatClient(), new RunSessionCoordinator());
    }

    private static AgentDef CreateAgent(string id, string providerId)
    {
        return new AgentDef(id, "Agent A", "You are concise.", "Say foo.", providerId, "model-a", null, []);
    }

    private static ProviderDef CreateProvider(string id)
    {
        return new ProviderDef(id, "Provider A", "https://provider.example.test/v1", "model-a");
    }

    private static ICommandItem GetRequiredEmptyContent(AgentsListPage page)
    {
        return page.EmptyContent ?? throw new InvalidOperationException("Expected EmptyContent to be set.");
    }

    private static T GetCommand<T>(ICommandItem item) where T : class
    {
        object command = GetProperty<object>(item, "Command");
        return Assert.IsType<T>(command);
    }

    private static T GetProperty<T>(object target, string propertyName)
    {
        PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing property {propertyName}.");
        return (T)property.GetValue(target)!;
    }

    private sealed class EmptyLlmChatClient : ILlmChatClient
    {
        public async IAsyncEnumerable<string> StreamAsync(ChatRequest request, ProviderDef provider, string apiKey, CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class TempStoreDirectory : IDisposable
    {
        private readonly string _rootPath;

        internal TempStoreDirectory()
        {
            _rootPath = Path.Combine(Path.GetTempPath(), $"little-agents-empty-content-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_rootPath);
        }

        internal AgentStore CreateAgentStore() => new(Path.Combine(_rootPath, "agents.json"));

        internal ProviderStore CreateProviderStore() => new(Path.Combine(_rootPath, "providers.json"));

        public void Dispose()
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }
    }
}
