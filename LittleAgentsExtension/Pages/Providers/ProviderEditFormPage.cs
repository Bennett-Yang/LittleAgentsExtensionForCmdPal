using LittleAgentsExtension.Common;
using LittleAgentsExtension.Storage;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LittleAgentsExtension;

internal sealed partial class ProviderEditFormPage : ContentPage
{
    private readonly ProviderEditForm _form;

    public ProviderEditFormPage(ProviderStore providers, ISecretStore secrets, ProviderDef? existing)
    {
        _form = new ProviderEditForm(providers, secrets, existing);
        Title = existing == null ? "New provider" : "Edit provider";
        Icon = Icons.Provider;
    }

    public override IContent[] GetContent() => [_form];
}

internal sealed partial class ProviderEditForm : FormContent
{
    private const string BaseUrlHint = "Include the /v1 (or your provider's API root path). Examples: https://api.openai.com/v1 · https://openrouter.ai/api/v1 · http://localhost:11434/v1";

    private readonly ProviderStore _providers;
    private readonly ISecretStore _secrets;
    private readonly ProviderDef? _existing;

    public ProviderEditForm(ProviderStore providers, ISecretStore secrets, ProviderDef? existing)
    {
        _providers = providers;
        _secrets = secrets;
        _existing = existing;

        TemplateJson = $$"""
{
    "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
    "type": "AdaptiveCard",
    "version": "1.6",
    "body": [
        {
            "type": "Input.Text",
            "id": "Name",
            "label": "Name",
            "value": {{Quote(existing?.Name ?? string.Empty)}},
            "isRequired": true,
            "errorMessage": "Name is required"
        },
        {
            "type": "Input.Text",
            "id": "BaseUrl",
            "label": "Base URL",
            "style":"url",
            "value": {{Quote(existing?.BaseUrl ?? string.Empty)}},
            "isRequired": true,
            "errorMessage": "Base URL is required",
            "placeholder": "https://api.openai.com/v1"
        },
        {
            "type": "TextBlock",
            "text": {{Quote(BaseUrlHint)}},
            "isSubtle": true,
            "size": "Small",
            "wrap": true
        },
        {
            "type": "Input.Text",
            "id": "ApiKey",
            "label": "API key",
            "style":"password"
        },
        {
            "type": "Input.Text",
            "id": "DefaultModel",
            "label": "Default model",
            "value": {{Quote(existing?.DefaultModel ?? string.Empty)}},
            "isRequired": false
        }
    ],
    "actions": [
        {
            "type": "Action.Submit",
            "title": "Save"
        }
    ]
}
""";
    }

    public override CommandResult SubmitForm(string payload)
    {
        JsonObject? formInput = JsonNode.Parse(payload)?.AsObject();
        if (formInput is null)
        {
            return CommandResult.KeepOpen();
        }

        string name = GetString(formInput, "Name").Trim();
        string baseUrl = GetString(formInput, "BaseUrl").Trim();
        string apiKey = GetString(formInput, "ApiKey").Trim();
        string defaultModel = GetString(formInput, "DefaultModel").Trim();

        if (name.Length == 0)
        {
            return ShowToastKeepOpen("Name is required");
        }

        if (!IsSupportedBaseUrl(baseUrl))
        {
            return ShowToastKeepOpen("Base URL must be an absolute http or https URL");
        }

        if (_existing is null && apiKey.Length == 0)
        {
            return ShowApiKeyRequired();
        }

        string providerId = _existing?.Id ?? Guid.NewGuid().ToString("N");
        ProviderDef provider = new(providerId, name, baseUrl, defaultModel.Length == 0 ? null : defaultModel);

        if (apiKey.Length > 0)
        {
            _secrets.Set(providerId, apiKey);
            AssertProvidersJsonDoesNotContain(apiKey);
        }

        _providers.Upsert(provider);

        return CommandResult.GoBack();
    }

    private static bool IsSupportedBaseUrl(string baseUrl)
    {
        return Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string GetString(JsonObject formInput, string key)
    {
        return formInput[key]?.ToString() ?? string.Empty;
    }

    private static CommandResult ShowApiKeyRequired()
    {
        CommandResult result = CommandResult.ShowToast("API key is required");
        if (result.Args is ToastArgs toast)
        {
            toast.Result = CommandResult.KeepOpen();
        }

        return result;
    }

    private static CommandResult ShowToastKeepOpen(string message)
    {
        return CommandResult.ShowToast(new ToastArgs()
        {
            Message = message,
            Result = CommandResult.KeepOpen(),
        });
    }

    private static string Quote(string value)
    {
        return $"\"{JsonEncodedText.Encode(value)}\"";
    }

    [Conditional("DEBUG")]
    private static void AssertProvidersJsonDoesNotContain(string apiKey)
    {
        string providersPath = Path.Combine(PathHelper.LocalStateDir, "providers.json");
        if (File.Exists(providersPath))
        {
            Debug.Assert(!File.ReadAllText(providersPath).Contains(apiKey, StringComparison.Ordinal));
        }
    }
}
