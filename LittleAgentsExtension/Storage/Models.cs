namespace LittleAgentsExtension.Storage;

internal sealed record AgentDef(string Id, string Name, string SystemPrompt, string UserTemplate, string ProviderId, string Model, string? Icon, string[] Tags);

internal sealed record ProviderDef(string Id, string Name, string BaseUrl, string? DefaultModel);

internal sealed record StoredChatMessage(string Role, string Content);

internal sealed record AgentsFile(int SchemaVersion, AgentDef[] Agents);

internal sealed record ProvidersFile(int SchemaVersion, ProviderDef[] Providers);
