using System.Collections;
using System.Reflection;
using LittleAgentsExtension.Llm;
using LittleAgentsExtension.Storage;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Xunit;

namespace LittleAgentsExtension.Tests;

public sealed partial class AgentsListPageInvocationTests
{
    private const string ApiKey = "sk-test";

    [Fact]
    public void Valid_provider_and_key_uses_direct_chat_run_page_command()
    {
        using TempStoreDirectory tempStore = new();
        AgentStore agents = tempStore.CreateAgentStore();
        ProviderStore providers = tempStore.CreateProviderStore();
        InMemorySecretStore secrets = new();
        agents.Save([CreateAgent("agent-a", "provider-a", "Say foo.")]);
        providers.Save([CreateProvider("provider-a")]);
        secrets.Set("provider-a", ApiKey);
        AgentsListPage page = new(agents, providers, secrets, new ScriptedLlmChatClient(_ => EmptyStream()), new RunSessionCoordinator());

        AssertDirectChatRunPage(page.GetItems()[0]);
    }

    [Fact]
    public void Missing_provider_returns_toast_containing_provider()
    {
        using TempStoreDirectory tempStore = new();
        AgentStore agents = tempStore.CreateAgentStore();
        ProviderStore providers = tempStore.CreateProviderStore();
        agents.Save([CreateAgent("agent-a", "missing-provider", "Say foo.")]);
        AgentsListPage page = new(agents, providers, new InMemorySecretStore(), new ScriptedLlmChatClient(_ => EmptyStream()), new RunSessionCoordinator());

        ICommandResult result = InvokePrimaryCommand(page.GetItems()[0]);

        AssertContainsString(result, "Provider");
    }

    [Fact]
    public void Missing_api_key_returns_toast_containing_api_key()
    {
        using TempStoreDirectory tempStore = new();
        AgentStore agents = tempStore.CreateAgentStore();
        ProviderStore providers = tempStore.CreateProviderStore();
        agents.Save([CreateAgent("agent-a", "provider-a", "Say foo.")]);
        providers.Save([CreateProvider("provider-a")]);
        AgentsListPage page = new(agents, providers, new InMemorySecretStore(), new ScriptedLlmChatClient(_ => EmptyStream()), new RunSessionCoordinator());

        ICommandResult result = InvokePrimaryCommand(page.GetItems()[0]);

        AssertContainsString(result, "API key");
    }

    [Fact]
    public void Store_changes_refresh_agent_command_after_provider_and_key_added()
    {
        using TempStoreDirectory tempStore = new();
        AgentStore agents = tempStore.CreateAgentStore();
        ProviderStore providers = tempStore.CreateProviderStore();
        InMemorySecretStore secrets = new();
        agents.Save([CreateAgent("agent-a", "provider-a", "Say foo.")]);
        AgentsListPage page = new(agents, providers, secrets, new ScriptedLlmChatClient(_ => EmptyStream()), new RunSessionCoordinator());
        ICommandResult missingProviderResult = InvokePrimaryCommand(page.GetItems()[0]);

        secrets.Set("provider-a", ApiKey);
        providers.Upsert(CreateProvider("provider-a"));

        AssertContainsString(missingProviderResult, "Provider");
        AssertDirectChatRunPage(page.GetItems()[0]);
    }

    [Fact]
    public async Task Invoke_passes_runtime_settings_to_chat_run_page()
    {
        using TempStoreDirectory tempStore = new();
        AgentStore agents = tempStore.CreateAgentStore();
        ProviderStore providers = tempStore.CreateProviderStore();
        InMemorySecretStore secrets = new();
        ScriptedLlmChatClient llm = new(call => EmptyStream());
        agents.Save([CreateAgent("agent-a", "provider-a", "Say foo.")]);
        providers.Save([CreateProvider("provider-a")]);
        secrets.Set("provider-a", ApiKey);
        AgentsListPage page = new(agents, providers, secrets, llm, new RunSessionCoordinator(), () => new RuntimeSettings("Prefix: ", 0.5));

        ChatRunPage runPage = AssertDirectChatRunPage(page.GetItems()[0]);
        StartStream(runPage, "hello");
        await GetStreamTask(runPage).WaitAsync(TimeSpan.FromSeconds(1));

        ChatRequest request = Assert.Single(llm.Requests);
        Assert.Equal(0.5, request.Temperature);
        Assert.Equal(new ChatMessage(ChatRole.System, "Prefix: You are concise."), request.Messages[0]);
    }

    [Fact]
    public async Task Shared_session_cancels_first_stream_when_second_input_page_renders()
    {
        using TempStoreDirectory tempStore = new();
        AgentStore agents = tempStore.CreateAgentStore();
        ProviderStore providers = tempStore.CreateProviderStore();
        InMemorySecretStore secrets = new();
        TaskCompletionSource firstStarted = Signal();
        TaskCompletionSource firstCancelled = Signal();
        RunSessionCoordinator sessions = new();
        agents.Save([
            CreateAgent("agent-a", "provider-a", "Say foo."),
            CreateAgent("agent-b", "provider-a", "Use {input}")
        ]);
        providers.Save([CreateProvider("provider-a")]);
        secrets.Set("provider-a", ApiKey);
        ScriptedLlmChatClient llm = new(call => WaitForCancellation(call, firstStarted, firstCancelled));
        AgentsListPage page = new(agents, providers, secrets, llm, sessions);
        IListItem[] items = page.GetItems();
        ChatRunPage firstPage = AssertDirectChatRunPage(items[0]);
        ChatRunPage secondPage = AssertDirectChatRunPage(items[1]);

        StartStream(firstPage, "first");
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        secondPage.GetContent();

        await firstCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(sessions.IsActive(firstPage));
        Assert.True(sessions.IsActive(secondPage));
        Assert.Null(GetField<Task?>(secondPage, "_streamTask"));
    }

}
