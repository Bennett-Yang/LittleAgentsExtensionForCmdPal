using System.Collections;
using System.Reflection;
using LittleAgentsExtension.Llm;
using LittleAgentsExtension.Storage;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Xunit;

namespace LittleAgentsExtension.Tests;

public sealed class AgentsListPagePinnedNavigationTests
{
    [Fact]
    public void New_agent_with_no_providers_navigates_to_provider_form()
    {
        using TempStoreDirectory tempStore = new();
        AgentsListPage page = CreatePage(tempStore.CreateAgentStore(), tempStore.CreateProviderStore());
        IListItem item = FindItemByTitle(page, "+ New Agent");

        AssertDirectPageCommand<ProviderEditFormPage>(item);
    }

    [Fact]
    public void New_agent_with_provider_navigates_to_agent_form()
    {
        using TempStoreDirectory tempStore = new();
        AgentStore agents = tempStore.CreateAgentStore();
        ProviderStore providers = tempStore.CreateProviderStore();
        providers.Save([CreateProvider("provider-a")]);
        AgentsListPage page = CreatePage(agents, providers);
        IListItem item = FindItemByTitle(page, "+ New Agent");

        AssertDirectPageCommand<AgentEditFormPage>(item);
    }

    [Fact]
    public void Manage_providers_navigates_to_providers_list()
    {
        using TempStoreDirectory tempStore = new();
        AgentsListPage page = CreatePage(tempStore.CreateAgentStore(), tempStore.CreateProviderStore());
        IListItem item = FindItemByTitle(page, "Manage Providers");

        AssertDirectPageCommand<ProvidersListPage>(item);
    }

    [Fact]
    public void Manage_providers_reuses_page_across_list_refreshes()
    {
        using TempStoreDirectory tempStore = new();
        AgentsListPage page = CreatePage(tempStore.CreateAgentStore(), tempStore.CreateProviderStore());

        ICommand first = GetProperty<ICommand>(FindItemByTitle(page, "Manage Providers"), "Command");
        ICommand second = GetProperty<ICommand>(FindItemByTitle(page, "Manage Providers"), "Command");

        Assert.Same(first, second);
    }

    [Fact]
    public void Settings_placeholder_is_removed_after_t40()
    {
        using TempStoreDirectory tempStore = new();
        AgentsListPage page = CreatePage(tempStore.CreateAgentStore(), tempStore.CreateProviderStore());

        IListItem[] items = page.GetItems();

        Assert.DoesNotContain(items, item => GetProperty<string>(item, "Title") == "Settings");
    }

    [Fact]
    public void Markdown_ticker_spike_is_not_exposed_in_agent_list()
    {
        using TempStoreDirectory tempStore = new();
        AgentsListPage page = CreatePage(tempStore.CreateAgentStore(), tempStore.CreateProviderStore());

        IListItem[] items = page.GetItems();

        Assert.DoesNotContain(items, item => GetProperty<string>(item, "Title") == "Markdown ticker spike");
    }

    [Fact]
    public void Agent_row_exposes_edit_page_in_more_commands()
    {
        using TempStoreDirectory tempStore = new();
        AgentStore agents = tempStore.CreateAgentStore();
        ProviderStore providers = tempStore.CreateProviderStore();
        agents.Save([new AgentDef("agent-a", "Agent A", "System", "User", "provider-a", "model-a", null, [])]);
        providers.Save([CreateProvider("provider-a")]);
        AgentsListPage page = CreatePage(agents, providers);
        IListItem agentItem = FindItemByTitle(page, "Agent A");

        CommandContextItem edit = GetMoreCommand(agentItem, "Edit");

        Assert.IsType<AgentEditFormPage>(GetProperty<ICommand>(edit, "Command"));
    }

    private static AgentsListPage CreatePage(AgentStore agents, ProviderStore providers)
    {
        return new AgentsListPage(agents, providers, new SecretStoreContractTests.InMemorySecretStore(), new EmptyLlmChatClient(), new RunSessionCoordinator());
    }

    private static ProviderDef CreateProvider(string id)
    {
        return new ProviderDef(id, "Provider A", "https://provider.example.test/v1", "model-a");
    }

    private static IListItem FindItemByTitle(AgentsListPage page, string title)
    {
        return page.GetItems().First(item => GetProperty<string>(item, "Title") == title);
    }

    private static void AssertDirectPageCommand<TPage>(IListItem item)
    {
        ICommand command = GetProperty<ICommand>(item, "Command");
        Assert.IsType<TPage>(command);
    }

    private static CommandContextItem GetMoreCommand(IListItem item, string title)
    {
        IContextItem[] commands = GetProperty<IContextItem[]>(item, "MoreCommands");
        return Assert.IsType<CommandContextItem>(commands.Single(command => GetProperty<string>(command, "Title") == title));
    }

    private static T GetProperty<T>(object target, string propertyName)
    {
        PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing property {propertyName}.");
        return (T)property.GetValue(target)!;
    }

    private static void AssertContainsString(object target, string expected)
    {
        Assert.True(ContainsString(target, expected), $"Expected {target.GetType().FullName} to contain '{expected}'.");
    }

    private static T? FindObject<T>(object target) where T : class
    {
        return FindObject<T>(target, new HashSet<object>(ReferenceEqualityComparer.Instance), depth: 0);
    }

    private static T? FindObject<T>(object? target, HashSet<object> visited, int depth) where T : class
    {
        if (target is null || depth > 6)
        {
            return null;
        }

        if (target is T match)
        {
            return match;
        }

        if (!ShouldDescend(target, visited))
        {
            return null;
        }

        foreach (object? child in GetChildren(target))
        {
            T? childMatch = FindObject<T>(child, visited, depth + 1);
            if (childMatch is not null)
            {
                return childMatch;
            }
        }

        return null;
    }

    private static bool ContainsString(object target, string expected)
    {
        return ContainsString(target, expected, new HashSet<object>(ReferenceEqualityComparer.Instance), depth: 0);
    }

    private static bool ContainsString(object? target, string expected, HashSet<object> visited, int depth)
    {
        if (target is null || depth > 6)
        {
            return false;
        }

        if (target is string text)
        {
            return text.Contains(expected, StringComparison.Ordinal);
        }

        if (!ShouldDescend(target, visited))
        {
            return false;
        }

        return GetChildren(target).Any(child => ContainsString(child, expected, visited, depth + 1));
    }

    private static bool ShouldDescend(object target, HashSet<object> visited)
    {
        Type targetType = target.GetType();
        return !targetType.IsPrimitive && !targetType.IsEnum && targetType != typeof(decimal) && visited.Add(target);
    }

    private static IEnumerable<object?> GetChildren(object target)
    {
        if (target is IEnumerable enumerable and not string)
        {
            foreach (object? item in enumerable)
            {
                yield return item;
            }
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type targetType = target.GetType();
        foreach (PropertyInfo property in targetType.GetProperties(flags).Where(property => property.GetIndexParameters().Length == 0))
        {
            object? value;
            try { value = property.GetValue(target); }
            catch (Exception exception) when (exception is TargetInvocationException or InvalidOperationException or NotSupportedException) { continue; }
            yield return value;
        }

        foreach (FieldInfo field in targetType.GetFields(flags))
        {
            yield return field.GetValue(target);
        }
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
            _rootPath = Path.Combine(Path.GetTempPath(), $"little-agents-pinned-nav-{Guid.NewGuid():N}");
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
