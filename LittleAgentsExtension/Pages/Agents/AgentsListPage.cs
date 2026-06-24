using LittleAgentsExtension.Llm;
using LittleAgentsExtension.Storage;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LittleAgentsExtension;

internal sealed partial class AgentsListPage : DynamicListPage
{
    private const string AgentIcon = "\uE945";

    private readonly AgentStore _agents;
    private AgentDef[] _cachedAgents;
    private ProviderDef[] _cachedProviders;

    public AgentsListPage(AgentStore agents, ProviderStore providers, ISecretStore secrets, ILlmChatClient llm, RunSessionCoordinator sessions)
    {
        _agents = agents;
        _ = secrets;
        _ = llm;
        _ = sessions;

        Title = "Little Agents";
        Icon = new IconInfo(AgentIcon);
        _cachedAgents = _agents.Load();
        _cachedProviders = providers.Load();

        agents.Changed += (_, _) =>
        {
            _cachedAgents = _agents.Load();
            RaiseItemsChanged(-1);
        };

        providers.Changed += (_, _) =>
        {
            _cachedProviders = providers.Load();
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

        items.Add(CreatePinnedItem("+ New Agent"));
        items.Add(CreatePinnedItem("Manage Providers"));
        items.Add(CreatePinnedItem("Settings"));

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
        string providerName = _cachedProviders.FirstOrDefault(provider => provider.Id == agent.ProviderId)?.Name ?? agent.ProviderId;

        return new ListItem(new AgentPlaceholderCommand(agent.Id, agent.Name))
        {
            Title = agent.Name,
            Subtitle = $"{providerName} · {agent.Model}",
            Icon = new IconInfo(agent.Icon ?? AgentIcon),
            Tags = agent.Tags.Select(tag => new Tag(tag)).ToArray(),
            MoreCommands = [new CommandContextItem(CreateDeletePage(agent)) { Title = "Delete" }],
        };
    }

    private static ListItem CreatePinnedItem(string title)
    {
        return new ListItem(new PinnedPlaceholderCommand(title))
        {
            Title = title,
            Icon = new IconInfo(AgentIcon),
        };
    }

    private static bool Contains(string value, string search) => value.Contains(search, StringComparison.OrdinalIgnoreCase);

    private ConfirmDeleteAgentPage CreateDeletePage(AgentDef agent) => new(_agents, agent)
    {
        Id = $"little-agents.agent.{agent.Id}.delete",
        Name = "Delete",
    };

    private sealed partial class AgentPlaceholderCommand : InvokableCommand
    {
        public AgentPlaceholderCommand(string agentId, string agentName)
        {
            Id = $"little-agents.agent.{agentId}";
            Name = agentName;
        }

        public override ICommandResult Invoke() => CommandResult.KeepOpen();
    }

    private sealed partial class PinnedPlaceholderCommand : InvokableCommand
    {
        public PinnedPlaceholderCommand(string title)
        {
            Id = $"little-agents.pinned.{GetStableKey(title)}";
            Name = title;
        }

        public override ICommandResult Invoke() => CommandResult.ShowToast("Wired in T30");

        private static string GetStableKey(string title) => title switch
        {
            "+ New Agent" => "new-agent",
            "Manage Providers" => "manage-providers",
            "Settings" => "settings",
            _ => title.Replace(' ', '-').ToLowerInvariant(),
        };
    }
}

internal sealed partial class ConfirmDeleteAgentPage : ContentPage
{
    private readonly ConfirmDeleteAgentForm _form;

    public ConfirmDeleteAgentPage(AgentStore agents, AgentDef agent)
    {
        _form = new ConfirmDeleteAgentForm(agents, agent);
        Title = $"Delete {agent.Name}";
        Icon = new IconInfo("\uE945");
    }

    public override IContent[] GetContent() => [_form];
}

internal sealed partial class ConfirmDeleteAgentForm : FormContent
{
    private readonly AgentStore _agents;
    private readonly AgentDef _agent;

    public ConfirmDeleteAgentForm(AgentStore agents, AgentDef agent)
    {
        _agents = agents;
        _agent = agent;
        TemplateJson = $$"""
{
    "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
    "type": "AdaptiveCard",
    "version": "1.6",
    "body": [
        {
            "type": "TextBlock",
            "text": "Delete agent '{{Quote(agent.Name)}}'? This cannot be undone.",
            "wrap": true
        }
    ],
    "actions": [
        {
            "type": "Action.Submit",
            "title": "Delete",
            "data": { "confirmed": true }
        },
        {
            "type": "Action.Submit",
            "title": "Cancel",
            "data": { "confirmed": false }
        }
    ]
}
""";
    }

    public override CommandResult SubmitForm(string payload)
    {
        if (IsConfirmed(payload))
        {
            _agents.Delete(_agent.Id);
        }

        return CommandResult.GoBack();
    }

    private static bool IsConfirmed(string payload)
    {
        try
        {
            JsonObject? input = JsonNode.Parse(payload)?.AsObject();
            return input?["confirmed"]?.GetValue<bool>() == true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static string Quote(string value) => JsonEncodedText.Encode(value).ToString();
}
