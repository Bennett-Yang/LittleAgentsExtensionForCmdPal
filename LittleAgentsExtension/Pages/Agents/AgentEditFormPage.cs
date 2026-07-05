using LittleAgentsExtension.Storage;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LittleAgentsExtension;

internal sealed partial class AgentEditFormPage : ContentPage
{
    private readonly AgentEditForm _form;
    public AgentEditFormPage(AgentStore agents, ProviderStore providers, AgentDef? existing)
    {
        _form = new AgentEditForm(agents, providers, existing);
        Title = existing == null ? "New agent" : "Edit agent";
        Icon = Icons.New;
    }
    public override IContent[] GetContent() => [_form];
}

internal sealed partial class AgentEditForm : FormContent
{
    private const string UserTemplateHint = "Use {input} for user input, {selection} for clipboard text";
    private readonly AgentStore _agents; private readonly ProviderStore _providers; private readonly AgentDef? _existing;
    public AgentEditForm(AgentStore agents, ProviderStore providers, AgentDef? existing)
    {
        _agents = agents; _providers = providers; _existing = existing;
        TemplateJson = $$"""{"$schema":"http://adaptivecards.io/schemas/adaptive-card.json","type":"AdaptiveCard","version":"1.6","body":[{"type":"Input.Text","id":"Name","label":"Name","value":{{Q(existing?.Name ?? string.Empty)}},"isRequired":true,"errorMessage":"Name is required"},{"type":"Input.Text","id":"SystemPrompt","label":"System prompt","value":{{Q(existing?.SystemPrompt ?? string.Empty)}},"isMultiline":true},{"type":"Input.Text","id":"UserTemplate","label":"User template","value":{{Q(existing?.UserTemplate ?? string.Empty)}},"isMultiline":true,"placeholder":{{Q(UserTemplateHint)}}},{"type":"Input.ChoiceSet","id":"ProviderId","label":"Provider","value":{{Q(existing?.ProviderId ?? string.Empty)}},"choices":[{{Choices(providers.Load())}}],"isRequired":true,"errorMessage":"Provider is required"},{"type":"Input.Text","id":"Model","label":"Model","value":{{Q(existing?.Model ?? string.Empty)}},"placeholder":"Leave empty to use provider default"},{"type":"Input.Text","id":"Icon","label":"Icon","value":{{Q(existing?.Icon ?? string.Empty)}}},{"type":"Input.Text","id":"Tags","label":"Tags","value":{{Q(existing is null ? string.Empty : string.Join(", ", existing.Tags))}},"placeholder":"Comma-separated"}],"actions":[{"type":"Action.Submit","title":"Save"}]}"""; if (existing is not null) { DataJson = $$"""{"Name":{{Q(existing.Name)}},"SystemPrompt":{{Q(existing.SystemPrompt)}},"UserTemplate":{{Q(existing.UserTemplate)}},"ProviderId":{{Q(existing.ProviderId)}},"Model":{{Q(existing.Model)}},"Icon":{{Q(existing.Icon ?? string.Empty)}},"Tags":{{Q(string.Join(", ", existing.Tags))}}}"""; }
    }
    public override CommandResult SubmitForm(string payload)
    {
        JsonObject? input = JsonNode.Parse(payload)?.AsObject();
        if (input is null) { return CommandResult.KeepOpen(); }
        string name = Get(input, "Name").Trim(), providerId = Get(input, "ProviderId").Trim(), model = Get(input, "Model").Trim();
        if (name.Length == 0) { return Toast("Name is required"); }
        if (providerId.Length == 0) { return Toast("Provider is required"); }
        if (model.Length == 0 && (model = _providers.Load().First(provider => provider.Id == providerId).DefaultModel?.Trim() ?? string.Empty).Length == 0) { return Toast("Model required (no default on this provider)"); }
        _agents.Upsert(new AgentDef(Id: _existing?.Id ?? Guid.NewGuid().ToString("N"), Name: name, SystemPrompt: Get(input, "SystemPrompt").Trim(), UserTemplate: Get(input, "UserTemplate").Trim(), ProviderId: providerId, Model: model, Icon: NullIfEmpty(Get(input, "Icon").Trim()), Tags: ParseTags(Get(input, "Tags"))));
        return CommandResult.GoBack();
    }
    private static string[] ParseTags(string tags) => tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
    private static string Get(JsonObject input, string key) => input[key]?.ToString() ?? string.Empty;
    private static CommandResult Toast(string message) => CommandResult.ShowToast(new ToastArgs() { Message = message, Result = CommandResult.KeepOpen(), });
    private static string Choices(ProviderDef[] providers) => string.Join(',', providers.Select(provider => $$"""{"title":{{Q(provider.Name)}} ,"value":{{Q(provider.Id)}}}"""));
    private static string Q(string value) => $"\"{JsonEncodedText.Encode(value)}\"";
}
