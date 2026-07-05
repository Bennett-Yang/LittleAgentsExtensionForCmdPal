using LittleAgentsExtension.Llm;
using LittleAgentsExtension.Storage;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LittleAgentsExtension;

internal sealed partial class AgentsListPage : DynamicListPage
{
    private readonly AgentStore _agents;
    private readonly ProviderStore _providers;
    private readonly ISecretStore _secrets;
    private readonly ILlmChatClient _llm;
    private readonly RunSessionCoordinator _sessions;
    private readonly Func<RuntimeSettings> _settings;
    private AgentDef[] _cachedAgents;
    private ProviderDef[] _cachedProviders;

    public AgentsListPage(AgentStore agents, ProviderStore providers, ISecretStore secrets, ILlmChatClient llm, RunSessionCoordinator sessions, Func<RuntimeSettings>? settings = null)
    {
        _agents = agents;
        _providers = providers;
        _secrets = secrets;
        _llm = llm;
        _sessions = sessions;
        _settings = settings ?? (() => RuntimeSettings.Default);

        Title = "Little Agents";
        Icon = Icons.AgentDefault;
        _cachedAgents = _agents.Load();
        _cachedProviders = providers.Load();
        RefreshEmptyContent();

        agents.Changed += (_, _) =>
        {
            _cachedAgents = _agents.Load();
            RefreshEmptyContent();
            RaiseItemsChanged(-1);
        };

        providers.Changed += (_, _) =>
        {
            _cachedProviders = providers.Load();
            RefreshEmptyContent();
            RaiseItemsChanged(-1);
        };
    }

    public override void UpdateSearchText(string oldSearch, string newSearch) => RaiseItemsChanged(-1);

    public override IListItem[] GetItems()
    {
        List<IListItem> items = _cachedAgents
            .Where(MatchesSearch)
            .Select(CreateAgentItem)
            .Cast<IListItem>()
            .ToList();

        items.Add(CreatePinnedItem(PinnedItemKind.NewAgent));
        items.Add(CreatePinnedItem(PinnedItemKind.ManageProviders));

        return items.ToArray();
    }

    private bool MatchesSearch(AgentDef agent)
    {
        string search = SearchText?.Trim() ?? string.Empty;
        if (search.Length == 0)
        {
            return true;
        }

        return Contains(agent.Name, search)
            || Contains(agent.ProviderId, search)
            || agent.Tags.Any(tag => Contains(tag, search));
    }

    private ListItem CreateAgentItem(AgentDef agent)
    {
        ProviderDef? provider = _cachedProviders.FirstOrDefault(candidate => candidate.Id == agent.ProviderId);
        string providerName = provider?.Name ?? agent.ProviderId;
        ICommand command = CreateAgentCommand(agent, provider);

        return new ListItem(command)
        {
            Title = agent.Name,
            Subtitle = $"{providerName} · {agent.Model}",
            Icon = agent.Icon is null ? Icons.AgentDefault : new IconInfo(agent.Icon),
            Tags = agent.Tags.Select(tag => new Tag(tag)).ToArray(),
            MoreCommands = [
                new CommandContextItem(CreateEditPage(agent)) { Title = "Edit" },
                new CommandContextItem(CreateDeletePage(agent)) { Title = "Delete" },
            ],
        };
    }

    private ICommand CreateAgentCommand(AgentDef agent, ProviderDef? provider)
    {
        if (provider is null)
        {
            return new AgentToastCommand($"Provider '{agent.ProviderId}' was not found.", agent);
        }

        string? key = _secrets.TryGet(agent.ProviderId);
        if (string.IsNullOrEmpty(key))
        {
            return new AgentToastCommand($"API key is missing for provider '{agent.ProviderId}'.", agent);
        }

        return new ChatRunPage(agent, provider, key, _llm, _sessions, new WindowsClipboardWriter(), _settings)
        {
            Id = $"little-agents.run.{agent.Id}.{Guid.NewGuid():N}",
            Name = agent.Name,
        };
    }

    private ListItem CreatePinnedItem(PinnedItemKind kind)
    {
        return kind switch
        {
            PinnedItemKind.NewAgent => CreateNewAgentPinnedItem(),
            PinnedItemKind.ManageProviders => new ListItem(CreateManageProvidersPage())
            {
                Title = "Manage Providers",
                Icon = Icons.Provider,
            },
            _ => new ListItem(new NoOpCommand()) { Title = "Unknown" },
        };
    }

    private ListItem CreateNewAgentPinnedItem()
    {
        ICommand targetPage = _cachedProviders.Length == 0 ? CreateNewProviderPage() : CreateNewAgentPage();
        ListItem item = new(targetPage)
        {
            Title = "+ New Agent",
            Icon = Icons.New,
        };

        if (_cachedProviders.Length == 0)
        {
            item.Subtitle = AddProviderFirstMessage;
        }

        return item;
    }

    private void RefreshEmptyContent()
    {
        EmptyContent = _cachedAgents.Length switch
        {
            0 when _cachedProviders.Length == 0 => CreateProviderOnboardingItem(),
            0 => CreateAgentOnboardingItem(),
            _ => null,
        };
    }

    private ListItem CreateProviderOnboardingItem() => new(CreateNewProviderPage())
    {
        Title = "Add a provider first",
        Subtitle = "You'll need an OpenAI-compatible endpoint and API key",
        Icon = Icons.Provider,
    };

    private ListItem CreateAgentOnboardingItem() => new(CreateNewAgentPage())
    {
        Title = "Create your first agent",
        Subtitle = "Define a system prompt and pick a provider",
        Icon = Icons.New,
    };

    private static bool Contains(string value, string search) => value.Contains(search, StringComparison.OrdinalIgnoreCase);

    private ProviderEditFormPage CreateNewProviderPage() => new(_providers, _secrets, existing: null)
    {
        Id = "little-agents.provider.new",
        Name = "Add a provider first",
    };

    private AgentEditFormPage CreateNewAgentPage() => new(_agents, _providers, existing: null)
    {
        Id = "little-agents.agent.new",
        Name = "Create your first agent",
    };

    private AgentEditFormPage CreateEditPage(AgentDef agent) => new(_agents, _providers, existing: agent)
    {
        Id = $"little-agents.agent.{agent.Id}.edit",
        Name = "Edit",
    };

    private ConfirmDeleteAgentPage CreateDeletePage(AgentDef agent) => new(_agents, agent)
    {
        Id = $"little-agents.agent.{agent.Id}.delete",
        Name = "Delete",
    };

    private sealed partial class AgentToastCommand : InvokableCommand
    {
        private readonly string _message;

        public AgentToastCommand(string message, AgentDef agent)
        {
            _message = message;
            Id = $"little-agents.agent.{agent.Id}";
            Name = agent.Name;
        }

        public override ICommandResult Invoke() => CommandResult.ShowToast(_message);
    }

        private enum PinnedItemKind
        {
            NewAgent,
            ManageProviders,
        }
}
