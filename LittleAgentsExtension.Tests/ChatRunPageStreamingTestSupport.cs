using System.Net;
using System.Reflection;
using LittleAgentsExtension.Llm;
using LittleAgentsExtension.Storage;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace LittleAgentsExtension.Tests;

public sealed partial class ChatRunPageStreamingTests
{
    private static ChatRunPage CreatePage(ILlmChatClient client, RunSessionCoordinator? session = null, Func<RuntimeSettings>? settings = null, string apiKey = ApiKey)
    {
        AgentDef agent = new("agent-a", "Agent A", "You are concise.", "Say foo.", "provider-a", "model-a", "\uE700", ["test"]);
        ProviderDef provider = new("provider-a", "Provider A", "https://provider.example.test/v1", "model-a");
        return new ChatRunPage(agent, provider, apiKey, client, session ?? new RunSessionCoordinator(), new FakeClipboardWriter(), settings, () => Task.FromResult<string?>(null));
    }

    private static async IAsyncEnumerable<string> EmitChunks(StreamCall call, params string[] chunks)
    {
        call.Started.SetResult();
        foreach (string chunk in chunks)
        {
            call.CancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return chunk;
        }
    }

    private static async IAsyncEnumerable<string> EmitThenWait(StreamCall call, TaskCompletionSource secondChunkSent, TaskCompletionSource release, params string[] chunks)
    {
        call.Started.SetResult();
        foreach (string chunk in chunks)
        {
            yield return chunk;
        }

        secondChunkSent.SetResult();
        await release.Task.WaitAsync(call.CancellationToken);
    }

    private static async IAsyncEnumerable<string> EmitLateIgnoringCancellation(StreamCall call, TaskCompletionSource oldChunkSent, TaskCompletionSource releaseOld)
    {
        call.Started.SetResult();
        yield return "old-start";
        oldChunkSent.SetResult();
        await releaseOld.Task;
        yield return "OLD-LATE";
    }

    private static async IAsyncEnumerable<string> WaitForCancellation(StreamCall call, TaskCompletionSource firstStarted, TaskCompletionSource firstCancelled)
    {
        call.Started.SetResult();
        firstStarted.SetResult();
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, call.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            firstCancelled.SetResult();
            throw;
        }

        yield break;
    }

    private static async IAsyncEnumerable<string> ThrowAfterChunk(StreamCall call, string chunk, Exception exception)
    {
        call.Started.SetResult();
        yield return chunk;
        await Task.Yield();
        throw exception;
    }

    private static void StartStream(ChatRunPage page, string message)
    {
        MethodInfo method = typeof(ChatRunPage).GetMethod("StartStream", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing StartStream.");
        method.Invoke(page, [message]);
    }

    private static Task GetStreamTask(ChatRunPage page) => GetField<Task?>(page, "_streamTask") ?? throw new InvalidOperationException("Missing stream task.");

    private static List<ChatMessage> GetHistory(ChatRunPage page) => GetField<List<ChatMessage>>(page, "_history");

    private static string GetBody(ChatRunPage page)
    {
        MarkdownContent output = GetField<MarkdownContent>(page, "_output");
        return (string)(typeof(MarkdownContent).GetProperty("Body") ?? throw new InvalidOperationException("MarkdownContent.Body is missing.")).GetValue(output)!;
    }

    private static T GetField<T>(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing field {fieldName}.");
        return (T)field.GetValue(target)!;
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class ScriptedLlmChatClient : ILlmChatClient
    {
        private readonly Func<StreamCall, IAsyncEnumerable<string>> _stream;
        public ScriptedLlmChatClient(Func<StreamCall, IAsyncEnumerable<string>> stream) => _stream = stream;
        public List<ChatRequest> Requests { get; } = new();
        public IAsyncEnumerable<string> StreamAsync(ChatRequest request, ProviderDef provider, string apiKey, CancellationToken ct)
        {
            Requests.Add(request);
            return _stream(new StreamCall(ct));
        }
    }

    private sealed class StreamCall(CancellationToken cancellationToken)
    {
        public CancellationToken CancellationToken { get; } = cancellationToken;
        public TaskCompletionSource Started { get; } = Signal();
    }

    private sealed class SyntheticStatusCodeException(HttpStatusCode statusCode, string message) : Exception(message)
    {
        public HttpStatusCode StatusCode { get; } = statusCode;
    }

    private sealed class FakeClipboardWriter : IClipboardWriter
    {
        public void SetText(string text) { }
    }
}
