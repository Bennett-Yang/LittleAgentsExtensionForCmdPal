using LittleAgentsExtension.Storage;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System.Text.Json;

namespace LittleAgentsExtension;

internal sealed partial class ConfirmDeleteAgentPage : ContentPage
{
    private readonly ConfirmDeleteAgentForm _form;

    public ConfirmDeleteAgentPage(AgentStore agents, AgentDef agent)
    {
        _form = new ConfirmDeleteAgentForm(agents, agent);
        Title = $"Delete {agent.Name}";
        Icon = Icons.Delete;
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
            "title": "Delete"
        }
    ]
}
""";
    }

    public override CommandResult SubmitForm(string payload)
    {
        _agents.Delete(_agent.Id);
        return CommandResult.GoBack();
    }

    private static string Quote(string value) => JsonEncodedText.Encode(value).ToString();
}
