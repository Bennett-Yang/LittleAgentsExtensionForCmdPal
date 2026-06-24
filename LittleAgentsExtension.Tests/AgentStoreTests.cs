using LittleAgentsExtension.Storage;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace LittleAgentsExtension.Tests;

public sealed class AgentStoreTests
{
    [Fact]
    public void Load_returns_empty_array_when_store_is_missing()
    {
        using TempStoreDirectory tempStore = new();

        AgentStore store = new(tempStore.StorePath);

        Assert.Empty(store.Load());
    }

    [Fact]
    public void Save_then_Load_round_trips_agent()
    {
        using TempStoreDirectory tempStore = new();

        AgentStore store = new(tempStore.StorePath);
        AgentDef agent = CreateAgent("agent-a", "Agent A", "provider-a", "model-a");

        store.Save([agent]);

        AgentDef[] loaded = store.Load();

        Assert.Single(loaded);
        AssertAgentEquivalent(agent, loaded[0]);
    }

    [Fact]
    public void Upsert_replaces_existing_agent_by_id()
    {
        using TempStoreDirectory tempStore = new();

        AgentStore store = new(tempStore.StorePath);
        store.Save([CreateAgent("agent-a", "Agent A", "provider-a", "model-a")]);

        AgentDef updated = CreateAgent("agent-a", "Agent A2", "provider-b", "model-b");

        store.Upsert(updated);

        AgentDef[] loaded = store.Load();

        Assert.Single(loaded);
        AssertAgentEquivalent(updated, loaded[0]);
    }

    [Fact]
    public void Delete_removes_existing_agent()
    {
        using TempStoreDirectory tempStore = new();

        AgentStore store = new(tempStore.StorePath);
        store.Save([
            CreateAgent("agent-a", "Agent A", "provider-a", "model-a"),
            CreateAgent("agent-b", "Agent B", "provider-b", "model-b")
        ]);

        store.Delete("agent-a");

        AgentDef[] loaded = store.Load();

        Assert.Single(loaded);
        AssertAgentEquivalent(CreateAgent("agent-b", "Agent B", "provider-b", "model-b"), loaded[0]);
    }

    [Fact]
    public void ConfirmDeleteAgentForm_cancel_payload_does_not_delete_agent()
    {
        using TempStoreDirectory tempStore = new();

        AgentStore store = new(tempStore.StorePath);
        AgentDef agent = CreateAgent("agent-a", "Agent A", "provider-a", "model-a");
        store.Save([agent]);

        ConfirmDeleteAgentForm form = new(store, agent);
        form.SubmitForm("{\"confirmed\":false}");

        Assert.Single(store.Load());
    }

    [Fact]
    public void ConfirmDeleteAgentForm_missing_confirmation_does_not_delete_agent()
    {
        using TempStoreDirectory tempStore = new();

        AgentStore store = new(tempStore.StorePath);
        AgentDef agent = CreateAgent("agent-a", "Agent A", "provider-a", "model-a");
        store.Save([agent]);

        ConfirmDeleteAgentForm form = new(store, agent);
        form.SubmitForm("{}");

        Assert.Single(store.Load());
    }

    [Fact]
    public void ConfirmDeleteAgentForm_confirmed_payload_deletes_agent()
    {
        using TempStoreDirectory tempStore = new();

        AgentStore store = new(tempStore.StorePath);
        AgentDef agent = CreateAgent("agent-a", "Agent A", "provider-a", "model-a");
        store.Save([agent]);

        ConfirmDeleteAgentForm form = new(store, agent);
        form.SubmitForm("{\"confirmed\":true}");

        Assert.Empty(store.Load());
    }

    [Fact]
    public void Load_throws_InvalidDataException_for_corrupt_json()
    {
        using TempStoreDirectory tempStore = new();

        File.WriteAllText(tempStore.StorePath, "{");

        AgentStore store = new(tempStore.StorePath);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => store.Load());

        Assert.IsType<JsonException>(exception.InnerException);
    }

    [Fact]
    public void Concurrent_saves_do_not_corrupt_file()
    {
        using TempStoreDirectory tempStore = new();

        AgentStore store = new(tempStore.StorePath);
        string[] expectedIds = Enumerable.Range(0, 24).Select(index => $"agent-{index}").ToArray();

        Parallel.For(0, expectedIds.Length, index =>
        {
            string agentId = expectedIds[index];
            store.Save([CreateAgent(agentId, $"Agent {index}", $"provider-{index}", $"model-{index}")]);
        });

        AgentDef[] loaded = store.Load();

        Assert.Single(loaded);
        Assert.Contains(loaded[0].Id, expectedIds);
    }

    private static AgentDef CreateAgent(string id, string name, string providerId, string model)
    {
        return new AgentDef(id, name, "You are concise.", "Say foo.", providerId, model, null, ["test"]);
    }

    private static void AssertAgentEquivalent(AgentDef expected, AgentDef actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.SystemPrompt, actual.SystemPrompt);
        Assert.Equal(expected.UserTemplate, actual.UserTemplate);
        Assert.Equal(expected.ProviderId, actual.ProviderId);
        Assert.Equal(expected.Model, actual.Model);
        Assert.Equal(expected.Icon, actual.Icon);
        Assert.Equal(expected.Tags, actual.Tags);
    }

    private sealed class TempStoreDirectory : IDisposable
    {
        private readonly string _rootPath;

        internal TempStoreDirectory()
        {
            _rootPath = Path.GetTempFileName();
            File.Delete(_rootPath);
            Directory.CreateDirectory(_rootPath);
        }

        internal string StorePath => Path.Combine(_rootPath, "agents.json");

        public void Dispose()
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }
    }
}
