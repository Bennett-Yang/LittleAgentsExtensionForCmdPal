using System.Collections;
using System.Reflection;
using LittleAgentsExtension.Storage;
using Xunit;

namespace LittleAgentsExtension.Tests;

public sealed class ProviderDeleteOrphanTests
{
    [Fact]
    public void Delete_provider_without_referencing_agents_removes_provider_and_secret()
    {
        using TempStoreDirectory tempStore = new();
        AgentStore agents = tempStore.CreateAgentStore();
        ProviderStore providers = tempStore.CreateProviderStore();
        SecretStoreContractTests.InMemorySecretStore secrets = new();
        ProviderDef provider = CreateProvider("provider-a");
        providers.Save([provider]);
        secrets.Set(provider.Id, "secret-a");
        ConfirmDeleteProviderForm form = new(providers, secrets, agents, provider);

        form.SubmitForm("{}");

        Assert.Empty(providers.Load());
        Assert.Null(secrets.TryGet(provider.Id));
    }

    [Fact]
    public void Delete_provider_with_two_referencing_agents_keeps_provider_and_secret_and_names_agents()
    {
        using TempStoreDirectory tempStore = new();
        AgentStore agents = tempStore.CreateAgentStore();
        ProviderStore providers = tempStore.CreateProviderStore();
        SecretStoreContractTests.InMemorySecretStore secrets = new();
        ProviderDef provider = CreateProvider("provider-a");
        providers.Save([provider]);
        agents.Save([CreateAgent("agent-a", "Alpha", provider.Id), CreateAgent("agent-b", "Beta", provider.Id)]);
        secrets.Set(provider.Id, "secret-a");
        ConfirmDeleteProviderForm form = new(providers, secrets, agents, provider);

        object result = form.SubmitForm("{}");

        Assert.Single(providers.Load());
        Assert.Equal("secret-a", secrets.TryGet(provider.Id));
        AssertContainsString(result, "Cannot delete 'Provider A': 2 agent(s) reference it");
        AssertContainsString(result, "Alpha");
        AssertContainsString(result, "Beta");
    }

    [Fact]
    public void Delete_provider_with_five_referencing_agents_lists_first_three_with_suffix()
    {
        using TempStoreDirectory tempStore = new();
        AgentStore agents = tempStore.CreateAgentStore();
        ProviderStore providers = tempStore.CreateProviderStore();
        SecretStoreContractTests.InMemorySecretStore secrets = new();
        ProviderDef provider = CreateProvider("provider-a");
        providers.Save([provider]);
        agents.Save([
            CreateAgent("agent-a", "Alpha", provider.Id),
            CreateAgent("agent-b", "Beta", provider.Id),
            CreateAgent("agent-c", "Gamma", provider.Id),
            CreateAgent("agent-d", "Delta", provider.Id),
            CreateAgent("agent-e", "Epsilon", provider.Id)
        ]);
        secrets.Set(provider.Id, "secret-a");
        ConfirmDeleteProviderForm form = new(providers, secrets, agents, provider);

        object result = form.SubmitForm("{}");

        Assert.Single(providers.Load());
        Assert.Equal("secret-a", secrets.TryGet(provider.Id));
        AssertContainsString(result, "Cannot delete 'Provider A': 5 agent(s) reference it");
        AssertContainsString(result, "Alpha, Beta, Gamma, ...");
    }

    private static ProviderDef CreateProvider(string id)
    {
        return new ProviderDef(id, "Provider A", "https://provider.example.test/v1", "model-a");
    }

    private static AgentDef CreateAgent(string id, string name, string providerId)
    {
        return new AgentDef(id, name, "You are concise.", "Say foo.", providerId, "model-a", null, []);
    }

    private static void AssertContainsString(object target, string expected)
    {
        Assert.True(ContainsString(target, expected, new HashSet<object>(ReferenceEqualityComparer.Instance), depth: 0), $"Expected {target.GetType().FullName} to contain '{expected}'.");
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

        Type targetType = target.GetType();
        if (targetType.IsPrimitive || targetType.IsEnum || targetType == typeof(decimal) || !visited.Add(target))
        {
            return false;
        }

        if (target is IEnumerable enumerable)
        {
            foreach (object? item in enumerable)
            {
                if (ContainsString(item, expected, visited, depth + 1))
                {
                    return true;
                }
            }
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (PropertyInfo property in targetType.GetProperties(flags).Where(property => property.GetIndexParameters().Length == 0))
        {
            object? value;
            try { value = property.GetValue(target); }
            catch (Exception exception) when (exception is TargetInvocationException or InvalidOperationException or NotSupportedException) { continue; }
            if (ContainsString(value, expected, visited, depth + 1))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class TempStoreDirectory : IDisposable
    {
        private readonly string _rootPath;

        internal TempStoreDirectory()
        {
            _rootPath = Path.Combine(Path.GetTempPath(), $"little-agents-provider-delete-{Guid.NewGuid():N}");
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
