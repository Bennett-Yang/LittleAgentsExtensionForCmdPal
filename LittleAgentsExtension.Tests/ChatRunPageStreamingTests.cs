using System.Net.Http;
using System.Net;
using System.Reflection;
using System.Security.Authentication;
using LittleAgentsExtension.Llm;
using LittleAgentsExtension.Storage;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Xunit;

namespace LittleAgentsExtension.Tests;

public sealed class ChatRunPageStreamingTests
{
    private const string ApiKey = "sk-test-LOG-CANARY-99999";

    [Fact]
    public async Task StartStream_builds_request_and_appends_one_assistant_when_stream_succeeds()
    {
        ScriptedLlmChatClient client = new(call => EmitChunks(call, "one", "two", "three"));
        ChatRunPage page = CreatePage(client, settings: () => new RuntimeSettings("Prefix: ", 0.2));

        StartStream(page, "hello");
        await GetStreamTask(page).WaitAsync(TimeSpan.FromSeconds(1));

        ChatRequest request = Assert.Single(client.Requests);
        Assert.Equal("model-a", request.Model);
        Assert.Equal(0.2, request.Temperature);
        Assert.Collection(request.Messages,
            message => Assert.Equal(new ChatMessage(ChatRole.System, "Prefix: You are concise."), message),
            message => Assert.Equal(new ChatMessage(ChatRole.User, "hello"), message));
        Assert.Equal("onetwothree", GetField<string>(page, "_lastAssistantText"));
        Assert.Contains("onetwothree", GetBody(page), StringComparison.Ordinal);
        Assert.Collection(GetHistory(page),
            message => Assert.Equal(new ChatMessage(ChatRole.User, "hello"), message),
            message => Assert.Equal(new ChatMessage(ChatRole.Assistant, "onetwothree"), message));
    }

    [Fact]
    public async Task StartStream_marks_stopped_and_keeps_only_user_history_when_current_stream_is_cancelled()
    {
        TaskCompletionSource secondChunkSent = Signal();
        TaskCompletionSource release = Signal();
        ScriptedLlmChatClient client = new(call => EmitThenWait(call, secondChunkSent, release, "one", "two"));
        ChatRunPage page = CreatePage(client);

        StartStream(page, "hello");
        await secondChunkSent.Task.WaitAsync(TimeSpan.FromSeconds(1));
        GetField<CancellationTokenSource>(page, "_cts").Cancel();
        release.SetResult();
        await GetStreamTask(page).WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Contains("_(stopped)_", GetBody(page), StringComparison.Ordinal);
        Assert.Collection(GetHistory(page), message => Assert.Equal(new ChatMessage(ChatRole.User, "hello"), message));
    }

    [Fact]
    public async Task StartStream_maps_network_status_and_tls_errors_without_leaking_api_key()
    {
        await AssertErrorAsync(new HttpRequestException("DNS down"), "> **Network error:** DNS down", expectedToast: "Network error");
        await AssertStatusErrorAsync();
        await AssertErrorAsync(new SyntheticStatusCodeException(HttpStatusCode.PaymentRequired, "pay " + ApiKey), "> **Error 402:** pay ***", forbidden: "pay " + ApiKey, expectedToast: "Error 402");
        await AssertErrorAsync(new HttpRequestException("network", new AuthenticationException("cert chain " + ApiKey)), "> **Provider TLS certificate rejected.**", forbidden: "> **Network error:**", expectedToast: "TLS rejected");
    }

    [Fact]
    public void MapErrorToToast_uses_same_priority_and_short_status_text()
    {
        Assert.Equal("TLS rejected", InvokeMapErrorToToast(new HttpRequestException("network", new AuthenticationException("cert chain"))));
        Assert.Equal("Error 402", InvokeMapErrorToToast(new SyntheticStatusCodeException(HttpStatusCode.PaymentRequired, "pay " + ApiKey)));
        Assert.Equal("Network error", InvokeMapErrorToToast(new HttpRequestException("DNS down")));
        Assert.Equal("Error", InvokeMapErrorToToast(new InvalidOperationException("boom " + ApiKey)));
    }

    [Fact]
    public async Task Superseded_stream_cannot_append_assistant_or_update_output_after_new_stream_starts()
    {
        TaskCompletionSource oldChunkSent = Signal();
        TaskCompletionSource releaseOld = Signal();
        int callIndex = 0;
        ScriptedLlmChatClient client = new(call => Interlocked.Increment(ref callIndex) == 1
            ? EmitLateIgnoringCancellation(call, oldChunkSent, releaseOld)
            : EmitChunks(call, "new-done"));
        ChatRunPage page = CreatePage(client);

        StartStream(page, "old");
        await oldChunkSent.Task.WaitAsync(TimeSpan.FromSeconds(1));
        StartStream(page, "new");
        releaseOld.SetResult();
        await GetStreamTask(page).WaitAsync(TimeSpan.FromSeconds(1));

        Assert.DoesNotContain("OLD-LATE", GetBody(page), StringComparison.Ordinal);
        Assert.Collection(GetHistory(page),
            message => Assert.Equal(new ChatMessage(ChatRole.User, "old"), message),
            message => Assert.Equal(new ChatMessage(ChatRole.User, "new"), message),
            message => Assert.Equal(new ChatMessage(ChatRole.Assistant, "new-done"), message));
        Assert.Null(GetField<CancellationTokenSource?>(page, "_cts"));
    }

    [Fact]
    public async Task Shared_session_cancels_previous_stream_when_second_page_starts_streaming()
    {
        RunSessionCoordinator session = new();
        TaskCompletionSource firstStarted = Signal();
        TaskCompletionSource firstCancelled = Signal();
        ChatRunPage first = CreatePage(new ScriptedLlmChatClient(call => WaitForCancellation(call, firstStarted, firstCancelled)), session);
        ChatRunPage second = CreatePage(new ScriptedLlmChatClient(call => EmitChunks(call, "second")), session);

        StartStream(first, "first");
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        StartStream(second, "second");
        await firstCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await GetStreamTask(second).WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(session.IsActive(second));
        Assert.Collection(GetHistory(second),
            message => Assert.Equal(new ChatMessage(ChatRole.User, "second"), message),
            message => Assert.Equal(new ChatMessage(ChatRole.Assistant, "second"), message));
    }

    private static async Task AssertErrorAsync(Exception exception, string expected, string? forbidden = null, string? expectedToast = null)
    {
        ScriptedLlmChatClient client = new(call => ThrowAfterChunk(call, "visible " + ApiKey, exception));
        ChatRunPage page = CreatePage(client);

        StartStream(page, "hello");
        await GetStreamTask(page).WaitAsync(TimeSpan.FromSeconds(1));

        string body = GetBody(page);
        Assert.Contains(expected, body, StringComparison.Ordinal);
        Assert.Contains("visible " + ApiKey, body, StringComparison.Ordinal);
        if (forbidden is not null)
        {
            Assert.DoesNotContain(forbidden, body, StringComparison.Ordinal);
        }
        if (expectedToast is not null)
        {
            Assert.Equal(expectedToast, GetField<string?>(page, "_lastToastText"));
        }

        Assert.Collection(GetHistory(page), message => Assert.Equal(new ChatMessage(ChatRole.User, "hello"), message));
    }

    private static async Task AssertStatusErrorAsync()
    {
        OpenAiChatClient client = new(OpenAiChatClientTests.FakeHttpHandler.Status(HttpStatusCode.TooManyRequests));
        ChatRunPage page = CreatePage(client);

        StartStream(page, "hello");
        await GetStreamTask(page).WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Contains("> **Error 429:**", GetBody(page), StringComparison.Ordinal);
        Assert.Equal("Error 429", GetField<string?>(page, "_lastToastText"));
        Assert.Collection(GetHistory(page), message => Assert.Equal(new ChatMessage(ChatRole.User, "hello"), message));
    }

    private static string InvokeMapErrorToToast(Exception exception)
    {
        MethodInfo method = typeof(ChatRunPage).GetMethod("MapErrorToToast", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing MapErrorToToast.");
        return (string)method.Invoke(null, [exception])!;
    }

    private static ChatRunPage CreatePage(ILlmChatClient client, RunSessionCoordinator? session = null, Func<RuntimeSettings>? settings = null)
    {
        AgentDef agent = new("agent-a", "Agent A", "You are concise.", "Say foo.", "provider-a", "model-a", "\uE700", ["test"]);
        ProviderDef provider = new("provider-a", "Provider A", "https://provider.example.test/v1", "model-a");
        return new ChatRunPage(agent, provider, ApiKey, client, session ?? new RunSessionCoordinator(), new FakeClipboardWriter(), settings, () => Task.FromResult<string?>(null));
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
    private sealed class SyntheticStatusCodeException(HttpStatusCode statusCode, string message) : Exception(message) { public HttpStatusCode StatusCode { get; } = statusCode; }
    private sealed class FakeClipboardWriter : IClipboardWriter { public void SetText(string text) { } }
}
