using System.Text.Json.Serialization;

namespace LittleAgentsExtension.Storage;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AgentsFile))]
[JsonSerializable(typeof(ProvidersFile))]
[JsonSerializable(typeof(StoredChatMessage))]
internal partial class LittleAgentsJsonContext : JsonSerializerContext;
