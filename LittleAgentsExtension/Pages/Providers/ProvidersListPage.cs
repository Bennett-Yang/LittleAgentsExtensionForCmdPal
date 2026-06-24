using LittleAgentsExtension.Storage;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace LittleAgentsExtension;

internal sealed partial class ProvidersListPage : DynamicListPage
{
    private const string ProviderIcon = "\uE968";

    private readonly ProviderStore _providers;
    private readonly ISecretStore _secrets;
    private readonly AgentStore _agents;
    private ProviderDef[] _cachedProviders;

    public ProvidersListPage(ProviderStore providers, ISecretStore secrets, AgentStore agents)
    {
        _providers = providers;
        _secrets = secrets;
        _agents = agents;
        _cachedProviders = providers.Load();

        Title = "Providers";
        Icon = new IconInfo(ProviderIcon);

        providers.Changed += (_, _) =>
        {
            _cachedProviders = providers.Load();
            RaiseItemsChanged(-1);
        };
    }

    public override void UpdateSearchText(string oldSearch, string newSearch) => RaiseItemsChanged(-1);

    public override IListItem[] GetItems()
    {
        List<IListItem> items = _cachedProviders
            .Where(MatchesSearch)
            .Select(CreateProviderItem)
            .Cast<IListItem>()
            .ToList();

        items.Add(CreatePinnedItem());
        return items.ToArray();
    }

    private bool MatchesSearch(ProviderDef provider)
    {
        string search = SearchText?.Trim() ?? string.Empty;
        if (search.Length == 0)
        {
            return true;
        }

        return Contains(provider.Name, search) || Contains(provider.BaseUrl, search);
    }

    private ListItem CreateProviderItem(ProviderDef provider)
    {
        return new ListItem(CreateEditPage(provider))
        {
            Title = provider.Name,
            Subtitle = provider.BaseUrl,
            Icon = new IconInfo(ProviderIcon),
            MoreCommands = [new CommandContextItem(CreateDeletePage(provider)) { Title = "Delete" }],
        };
    }

    private ListItem CreatePinnedItem()
    {
        const string title = "+ New Provider";
        return new ListItem(CreateNewPage())
        {
            Title = title,
            Icon = new IconInfo(ProviderIcon),
        };
    }

    private static bool Contains(string value, string search) => value.Contains(search, StringComparison.OrdinalIgnoreCase);

    private ProviderEditFormPage CreateEditPage(ProviderDef provider) => new(_providers, _secrets, existing: provider)
    {
        Id = $"little-agents.provider.{provider.Id}.edit",
        Name = provider.Name,
    };

    private ProviderEditFormPage CreateNewPage() => new(_providers, _secrets, existing: null)
    {
        Id = "little-agents.provider.new",
        Name = "+ New Provider",
    };

    private ConfirmDeleteProviderPage CreateDeletePage(ProviderDef provider) => new(_providers, _secrets, _agents, provider)
    {
        Id = $"little-agents.provider.{provider.Id}.delete",
        Name = "Delete",
    };
}

internal sealed partial class ConfirmDeleteProviderPage : ContentPage
{
    private readonly ConfirmDeleteProviderForm _form;

    public ConfirmDeleteProviderPage(ProviderStore providers, ISecretStore secrets, AgentStore agents, ProviderDef provider)
    {
        _form = new ConfirmDeleteProviderForm(providers, secrets, agents, provider);
        Title = $"Delete {provider.Name}";
        Icon = new IconInfo("\uE968");
    }

    public override IContent[] GetContent() => [_form];
}

internal sealed partial class ConfirmDeleteProviderForm : FormContent
{
    private readonly ProviderStore _providers;
    private readonly ISecretStore _secrets;
    private readonly AgentStore _agents;
    private readonly ProviderDef _provider;

    public ConfirmDeleteProviderForm(ProviderStore providers, ISecretStore secrets, AgentStore agents, ProviderDef provider)
    {
        _providers = providers;
        _secrets = secrets;
        _agents = agents;
        _provider = provider;
        TemplateJson = $$"""
{
    "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
    "type": "AdaptiveCard",
    "version": "1.6",
    "body": [
        {
            "type": "TextBlock",
            "text": "Delete {{Quote(provider.Name)}}?",
            "wrap": true
        }
    ],
    "actions": [
        {
            "type": "Action.Submit",
            "title": "Delete"
        }
    ]
}
""";
    }

    public override CommandResult SubmitForm(string payload)
    {
        string[] orphans = _agents.Load()
            .Where(agent => agent.ProviderId == _provider.Id)
            .Select(agent => agent.Name)
            .ToArray();

        if (orphans.Length > 0)
        {
            string names = string.Join(", ", orphans.Take(3));
            string suffix = orphans.Length > 3 ? ", ..." : string.Empty;
            return ToastKeepOpen($"Cannot delete '{_provider.Name}': {orphans.Length} agent(s) reference it - {names}{suffix} - reassign or delete them first");
        }

        _providers.Delete(_provider.Id);
        _secrets.Delete(_provider.Id);
        return CommandResult.GoBack();
    }

    private static CommandResult ToastKeepOpen(string message)
    {
        return CommandResult.ShowToast(new ToastArgs()
        {
            Message = message,
            Result = CommandResult.KeepOpen(),
        });
    }

    private static string Quote(string value) => JsonEncodedText.Encode(value).ToString();
}
