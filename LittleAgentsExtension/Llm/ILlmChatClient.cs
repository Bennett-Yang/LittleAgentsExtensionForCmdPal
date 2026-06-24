using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using LittleAgentsExtension.Storage;

namespace LittleAgentsExtension.Llm;

internal interface ILlmChatClient
{
    IAsyncEnumerable<string> StreamAsync(ChatRequest request, ProviderDef provider, string apiKey, [EnumeratorCancellation] CancellationToken ct);
}
