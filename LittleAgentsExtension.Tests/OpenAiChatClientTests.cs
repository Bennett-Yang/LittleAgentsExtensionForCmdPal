using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LittleAgentsExtension.Llm;
using LittleAgentsExtension.Storage;
using System.ClientModel;
using Xunit;

namespace LittleAgentsExtension.Tests;

/// <summary>
/// These tests use the SDK-supported OpenAIClientOptions.Transport hook, backed by HttpClientPipelineTransport and FakeHttpHandler.
/// </summary>
public sealed partial class OpenAiChatClientTests
{
    private const string ApiKey = "sk-test-LOG-CANARY-99999";

    [Fact]
    public async Task StreamAsync_sends_expected_request_uri_when_base_url_includes_v1()
    {
        FakeHttpHandler handler = FakeHttpHandler.SseChunks(1);
        OpenAiChatClient client = new(handler);

        await CollectAsync(client.StreamAsync(CreateRequest(), CreateProvider("https://api.example.com/v1"), ApiKey, CancellationToken.None));

        Assert.Equal("https://api.example.com/v1/chat/completions", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task StreamAsync_request_uri_does_not_duplicate_v1_when_base_url_includes_v1()
    {
        FakeHttpHandler handler = FakeHttpHandler.SseChunks(1);
        OpenAiChatClient client = new(handler);

        await CollectAsync(client.StreamAsync(CreateRequest(), CreateProvider("https://api.example.com/v1"), ApiKey, CancellationToken.None));

        Assert.DoesNotMatch(new Regex("^.+/v1/v1/chat/completions$"), handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task StreamAsync_sends_bearer_authorization_header()
    {
        FakeHttpHandler handler = FakeHttpHandler.SseChunks(1);
        OpenAiChatClient client = new(handler);

        await CollectAsync(client.StreamAsync(CreateRequest(), CreateProvider(), ApiKey, CancellationToken.None));

        AuthenticationHeaderValue authorization = handler.LastRequest!.Headers.Authorization!;
        Assert.Equal("Bearer", authorization.Scheme);
        Assert.Equal(ApiKey, authorization.Parameter);
    }

    [Fact]
    public async Task StreamAsync_sends_stream_true_and_messages_in_request_body()
    {
        FakeHttpHandler handler = FakeHttpHandler.SseChunks(1);
        OpenAiChatClient client = new(handler);

        await CollectAsync(client.StreamAsync(CreateRequest(), CreateProvider(), ApiKey, CancellationToken.None));

        using JsonDocument body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
        JsonElement messages = body.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("You are concise.", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("Say foo.", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task StreamAsync_yields_three_sse_chunks_in_order()
    {
        FakeHttpHandler handler = FakeHttpHandler.SseChunks(3);
        OpenAiChatClient client = new(handler);

        string[] chunks = await CollectAsync(client.StreamAsync(CreateRequest(), CreateProvider(), ApiKey, CancellationToken.None));

        Assert.Equal(new[] { "foo", "foo", "foo" }, chunks);
    }

    [Fact]
    public async Task StreamAsync_stops_within_200ms_when_cancelled_mid_stream()
    {
        FakeHttpHandler handler = FakeHttpHandler.CancellableAfterFirstChunk();
        OpenAiChatClient client = new(handler);
        using CancellationTokenSource cts = new();
        List<string> chunks = new();

        await using IAsyncEnumerator<string> enumerator = client.StreamAsync(CreateRequest(), CreateProvider(), ApiKey, cts.Token).GetAsyncEnumerator(cts.Token);
        Assert.True(await enumerator.MoveNextAsync());
        chunks.Add(enumerator.Current);
        Task<bool> moveNext = Task.Run(async () => await enumerator.MoveNextAsync().AsTask());
        await handler.ReadPending.Task.WaitAsync(TimeSpan.FromMilliseconds(200));
        cts.Cancel();
        Task completed = await Task.WhenAny(moveNext, Task.Delay(TimeSpan.FromMilliseconds(200)));

        Assert.Same(moveNext, completed);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await moveNext);
        Assert.Equal(new[] { "foo" }, chunks);
        Assert.True(handler.LastRequestCancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task StreamAsync_401_exception_message_does_not_include_api_key_canary()
    {
        FakeHttpHandler handler = FakeHttpHandler.Status(HttpStatusCode.Unauthorized);
        OpenAiChatClient client = new(handler);

        ClientResultException exception = await Assert.ThrowsAsync<ClientResultException>(
            async () => await CollectAsync(client.StreamAsync(CreateRequest(), CreateProvider(), ApiKey, CancellationToken.None)));

        Assert.Equal(401, exception.Status);
        Assert.DoesNotContain(ApiKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamAsync_429_exception_exposes_status()
    {
        FakeHttpHandler handler = FakeHttpHandler.Status(HttpStatusCode.TooManyRequests);
        OpenAiChatClient client = new(handler);

        ClientResultException exception = await Assert.ThrowsAsync<ClientResultException>(
            async () => await CollectAsync(client.StreamAsync(CreateRequest(), CreateProvider(), ApiKey, CancellationToken.None)));

        Assert.Equal(429, exception.Status);
    }

    [Fact]
    public async Task StreamAsync_network_down_bubbles_as_http_request_exception()
    {
        HttpRequestException expected = new("network down");
        FakeHttpHandler handler = FakeHttpHandler.Throws(expected);
        OpenAiChatClient client = new(handler);

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(
            async () => await CollectAsync(client.StreamAsync(CreateRequest(), CreateProvider(), ApiKey, CancellationToken.None)));

        Assert.Same(expected, exception);
    }

    [Fact]
    public async Task StreamAsync_tls_chain_failure_bubbles_with_authentication_exception_in_inner_chain()
    {
        HttpRequestException expected = new("tls failed", new AuthenticationException("chain failed"));
        FakeHttpHandler handler = FakeHttpHandler.Throws(expected);
        OpenAiChatClient client = new(handler);

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(
            async () => await CollectAsync(client.StreamAsync(CreateRequest(), CreateProvider(), ApiKey, CancellationToken.None)));

        Assert.Contains(Flatten(exception), current => current is AuthenticationException);
    }

    [Fact]
    public async Task StreamAsync_invalid_base_url_throws_invalid_operation_without_api_key_prefix()
    {
        OpenAiChatClient client = new();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CollectAsync(client.StreamAsync(CreateRequest(), CreateProvider("not-a-url"), ApiKey, CancellationToken.None)));

        Assert.DoesNotContain("sk-", exception.Message, StringComparison.Ordinal);
    }

    private static ChatRequest CreateRequest() => new(
        "gpt-test",
        new[]
        {
            new ChatMessage(ChatRole.System, "You are concise."),
            new ChatMessage(ChatRole.User, "Say foo."),
        },
        0.2);

    private static ProviderDef CreateProvider(string baseUrl = "https://api.example.com/v1") => new("openai", "OpenAI", baseUrl, "gpt-test");

    private static async Task<string[]> CollectAsync(IAsyncEnumerable<string> stream)
    {
        List<string> chunks = new();
        await foreach (string chunk in stream)
        {
            chunks.Add(chunk);
        }

        return chunks.ToArray();
    }

    private static IEnumerable<Exception> Flatten(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

}
