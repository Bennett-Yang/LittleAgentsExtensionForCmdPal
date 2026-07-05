using System.Net.Http;
using System.Net;
using System.Reflection;
using System.Security.Authentication;
using LittleAgentsExtension.Llm;
using LittleAgentsExtension.Storage;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Xunit;

namespace LittleAgentsExtension.Tests;

public sealed partial class ChatRunPageStreamingTests
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

}
