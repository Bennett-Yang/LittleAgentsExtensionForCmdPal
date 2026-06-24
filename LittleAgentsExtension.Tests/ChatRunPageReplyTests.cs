using System.Reflection;
using LittleAgentsExtension.Llm;
using LittleAgentsExtension.Storage;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Xunit;

namespace LittleAgentsExtension.Tests;

public sealed class ChatRunPageReplyTests
{
    private const string ApiKey = "sk-test";

    [Fact]
    public async Task ReplyCommand_submits_literal_follow_up_without_rereading_clipboard()
    {
        int callIndex = 0;
        int clipboardReads = 0;
        ScriptedLlmChatClient client = new(call => Interlocked.Increment(ref callIndex) == 1
            ? EmitChunks(call, "assistant", "-one")
            : EmitChunks(call, "assistant", "-two"));
        ChatRunPage page = CreatePage(client, userTemplate: "initial {selection}", readClipboardAsync: () =>
        {
            clipboardReads++;
            return Task.FromResult<string?>("selected text");
        });

        page.GetContent();
        await GetRequiredTask(page, "_initTask").WaitAsync(TimeSpan.FromSeconds(1));
        await GetStreamTask(page).WaitAsync(TimeSpan.FromSeconds(1));
        InvokeCommand(page, "Reply");
        RunInputForm form = Assert.IsType<RunInputForm>(Assert.Single(page.GetContent()));
        form.SubmitForm("{\"Input\":\"follow-up {selection}\"}");
        await GetStreamTask(page).WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(1, clipboardReads);
        Assert.Null(GetField<string?>(page, "_lastToastText"));
        Assert.Equal(2, client.Requests.Count);
        Assert.Collection(client.Requests[1].Messages,
            message => Assert.Equal(new ChatMessage(ChatRole.System, "You are concise."), message),
            message => Assert.Equal(new ChatMessage(ChatRole.User, "initial selected text"), message),
            message => Assert.Equal(new ChatMessage(ChatRole.Assistant, "assistant-one"), message),
            message => Assert.Equal(new ChatMessage(ChatRole.User, "follow-up {selection}"), message));
        Assert.Collection(GetHistory(page),
            message => Assert.Equal(new ChatMessage(ChatRole.User, "initial selected text"), message),
            message => Assert.Equal(new ChatMessage(ChatRole.Assistant, "assistant-one"), message),
            message => Assert.Equal(new ChatMessage(ChatRole.User, "follow-up {selection}"), message),
            message => Assert.Equal(new ChatMessage(ChatRole.Assistant, "assistant-two"), message));
    }

    [Fact]
    public async Task ReplyCommand_cancellation_preserves_user_reply_without_incomplete_assistant_turn()
    {
        TaskCompletionSource replyChunkSent = Signal();
        int callIndex = 0;
        ScriptedLlmChatClient client = new(call => Interlocked.Increment(ref callIndex) == 1
            ? EmitChunks(call, "assistant-one")
            : EmitThenWait(call, replyChunkSent, "partial-reply"));
        ChatRunPage page = CreatePage(client, userTemplate: "first prompt");

        page.GetContent();
        await GetRequiredTask(page, "_initTask").WaitAsync(TimeSpan.FromSeconds(1));
        await GetStreamTask(page).WaitAsync(TimeSpan.FromSeconds(1));
        InvokeCommand(page, "Reply");
        RunInputForm form = Assert.IsType<RunInputForm>(Assert.Single(page.GetContent()));
        form.SubmitForm("{\"Input\":\"second prompt\"}");
        await replyChunkSent.Task.WaitAsync(TimeSpan.FromSeconds(1));
        InvokeCommand(page, "Stop");
        await GetStreamTask(page).WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Contains("_(stopped)_", GetBody(page), StringComparison.Ordinal);
        Assert.Collection(GetHistory(page),
            message => Assert.Equal(new ChatMessage(ChatRole.User, "first prompt"), message),
            message => Assert.Equal(new ChatMessage(ChatRole.Assistant, "assistant-one"), message),
            message => Assert.Equal(new ChatMessage(ChatRole.User, "second prompt"), message));
    }

    private static ChatRunPage CreatePage(ILlmChatClient client, string userTemplate, Func<Task<string?>>? readClipboardAsync = null)
    {
        AgentDef agent = new("agent-a", "Agent A", "You are concise.", userTemplate, "provider-a", "model-a", "\uE700", ["test"]);
        ProviderDef provider = new("provider-a", "Provider A", "https://provider.example.test/v1", "model-a");
        return new ChatRunPage(agent, provider, ApiKey, client, new RunSessionCoordinator(), new FakeClipboardWriter(), readClipboardAsync: readClipboardAsync ?? (() => Task.FromResult<string?>(null)));
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

    private static async IAsyncEnumerable<string> EmitThenWait(StreamCall call, TaskCompletionSource chunkSent, params string[] chunks)
    {
        call.Started.SetResult();
        foreach (string chunk in chunks)
        {
            call.CancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return chunk;
        }

        chunkSent.SetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, call.CancellationToken);
    }

    private static Task GetStreamTask(ChatRunPage page) => GetField<Task?>(page, "_streamTask") ?? throw new InvalidOperationException("Missing stream task.");

    private static Task GetRequiredTask(object target, string fieldName) => GetField<Task?>(target, fieldName) ?? throw new InvalidOperationException($"Missing task {fieldName}.");

    private static List<ChatMessage> GetHistory(ChatRunPage page) => GetField<List<ChatMessage>>(page, "_history");

    private static string GetBody(ChatRunPage page)
    {
        MarkdownContent output = GetField<MarkdownContent>(page, "_output");
        return (string)(typeof(MarkdownContent).GetProperty("Body") ?? throw new InvalidOperationException("MarkdownContent.Body is missing.")).GetValue(output)!;
    }

    private static void InvokeCommand(ChatRunPage page, string title)
    {
        CommandContextItem item = page.Commands.Cast<CommandContextItem>().Single(command => command.Title == title);
        ICommand command = item.Command ?? throw new InvalidOperationException("Missing command.");
        InvokableCommand invokable = command as InvokableCommand ?? throw new InvalidOperationException("Command is not invokable.");
        invokable.Invoke();
    }

    private static T GetField<T>(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing field {fieldName}.");
        return (T)field.GetValue(target)!;
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class ScriptedLlmChatClient(Func<StreamCall, IAsyncEnumerable<string>> stream) : ILlmChatClient
    {
        public List<ChatRequest> Requests { get; } = new();

        public IAsyncEnumerable<string> StreamAsync(ChatRequest request, ProviderDef provider, string apiKey, CancellationToken ct)
        {
            Requests.Add(request);
            return stream(new StreamCall(ct));
        }
    }

    private sealed class StreamCall(CancellationToken cancellationToken)
    {
        public CancellationToken CancellationToken { get; } = cancellationToken;
        public TaskCompletionSource Started { get; } = Signal();
    }

    private sealed class FakeClipboardWriter : IClipboardWriter { public void SetText(string text) { } }
}
