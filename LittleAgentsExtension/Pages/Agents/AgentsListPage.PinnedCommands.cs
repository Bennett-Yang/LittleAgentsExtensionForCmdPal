namespace LittleAgentsExtension;

internal sealed partial class AgentsListPage
{
    private const string AddProviderFirstMessage = "Add a provider first";

    private ProvidersListPage CreateManageProvidersPage() => new(_providers, _secrets, _agents)
    {
        Id = "little-agents.providers",
        Name = "Manage Providers",
    };
}
