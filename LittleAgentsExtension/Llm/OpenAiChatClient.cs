using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using LittleAgentsExtension.Storage;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace LittleAgentsExtension.Llm;

internal sealed class OpenAiChatClient : ILlmChatClient
{
    private const string SecretPattern = @"(?i)(bearer\s+)?sk-[A-Za-z0-9_-]{4,}";
    private readonly HttpMessageHandler? _messageHandler;

    public OpenAiChatClient()
    {
    }

    internal OpenAiChatClient(HttpMessageHandler messageHandler)
    {
        _messageHandler = messageHandler;
    }

    public async IAsyncEnumerable<string> StreamAsync(
        ChatRequest request,
        ProviderDef provider,
        string apiKey,
        [EnumeratorCancellation] CancellationToken ct)
    {
        IChatClient client = CreateClient(request.Model, provider.BaseUrl, apiKey, _messageHandler);
        Microsoft.Extensions.AI.ChatMessage[] messages = ToMessages(request.Messages);
        ChatOptions options = new() { Temperature = (float?)request.Temperature };

        await using IAsyncEnumerator<ChatResponseUpdate> updates = client.GetStreamingResponseAsync(messages, options, ct).GetAsyncEnumerator(ct);
        while (await MoveNextAsync(updates))
        {
            ChatResponseUpdate update = updates.Current;
            string? text = update.Text;
            if (!string.IsNullOrEmpty(text))
            {
                yield return text;
            }
        }
    }

    private static IChatClient CreateClient(string model, string baseUrl, string apiKey, HttpMessageHandler? messageHandler)
    {
        try
        {
            return new OpenAI.Chat.ChatClient(
                model,
                new ApiKeyCredential(apiKey),
                CreateOptions(baseUrl, messageHandler)).AsIChatClient();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Provider misconfigured: {ScrubSecrets(exception.Message)}");
        }
    }

    private static OpenAIClientOptions CreateOptions(string baseUrl, HttpMessageHandler? messageHandler)
    {
        OpenAIClientOptions options = new() { Endpoint = new Uri(baseUrl) };
        if (messageHandler is not null)
        {
            options.Transport = new HttpClientPipelineTransport(new HttpClient(messageHandler));
            options.RetryPolicy = new ClientRetryPolicy(0);
        }

        return options;
    }

    private static async ValueTask<bool> MoveNextAsync(IAsyncEnumerator<ChatResponseUpdate> updates)
    {
        try
        {
            return await updates.MoveNextAsync();
        }
        catch (ClientResultException exception) when (exception.InnerException is HttpRequestException httpRequestException)
        {
            throw httpRequestException;
        }
    }

    private static Microsoft.Extensions.AI.ChatMessage[] ToMessages(ChatMessage[] messages)
    {
        Microsoft.Extensions.AI.ChatMessage[] converted = new Microsoft.Extensions.AI.ChatMessage[messages.Length];
        for (int index = 0; index < messages.Length; index++)
        {
            ChatMessage message = messages[index];
            converted[index] = new Microsoft.Extensions.AI.ChatMessage(ToRole(message.Role), message.Content);
        }

        return converted;
    }

    private static Microsoft.Extensions.AI.ChatRole ToRole(ChatRole role) => role switch
    {
        ChatRole.System => Microsoft.Extensions.AI.ChatRole.System,
        ChatRole.User => Microsoft.Extensions.AI.ChatRole.User,
        ChatRole.Assistant => Microsoft.Extensions.AI.ChatRole.Assistant,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    private static string ScrubSecrets(string message) => Regex.Replace(message, SecretPattern, "***");
}
