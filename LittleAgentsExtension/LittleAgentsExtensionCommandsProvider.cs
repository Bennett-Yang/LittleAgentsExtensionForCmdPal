// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using LittleAgentsExtension.Llm;
using LittleAgentsExtension.Storage;

namespace LittleAgentsExtension;

public partial class LittleAgentsExtensionCommandsProvider : CommandProvider
{
    private readonly AgentStore _agentStore;
    private readonly ProviderStore _providerStore;
    private readonly ISecretStore _secretStore;
    private readonly OpenAiChatClient _llmClient;
    private readonly RunSessionCoordinator _runSessions;
    private readonly ICommandItem[] _commands;

    public LittleAgentsExtensionCommandsProvider()
    {
        DisplayName = "Little Agents";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        _agentStore = new AgentStore();
        _providerStore = new ProviderStore();
        _secretStore = SecretStoreFactory.Create();
        _llmClient = new OpenAiChatClient();
        _runSessions = new RunSessionCoordinator();
        _commands = [
            new CommandItem(new AgentsListPage(_agentStore, _providerStore, _secretStore, _llmClient, _runSessions)) { Title = DisplayName },
        ];
    }

    public override ICommandItem[] TopLevelCommands()
    {
        return _commands;
    }

}
