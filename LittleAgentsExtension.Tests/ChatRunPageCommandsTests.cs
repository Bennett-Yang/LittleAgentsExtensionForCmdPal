using System.Reflection;
using LittleAgentsExtension.Llm;
using LittleAgentsExtension.Storage;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Xunit;

namespace LittleAgentsExtension.Tests;

public sealed class ChatRunPageCommandsTests
{
    private const string ApiKey = "sk-test";

    [Fact]
    public async Task CopyResultCommand_copies_last_assistant_text_after_completion()
    {
        RecordingClipboardWriter clipboard = new();
        ChatRunPage page = CreatePage(new ScriptedLlmChatClient(call => EmitChunks(call, "one", "two")), clipboard);

        StartStream(page, "hello");
        await GetStreamTask(page).WaitAsync(TimeSpan.FromSeconds(1));
        InvokeCommand(page, "Copy result");

        Assert.Equal(["onetwo"], clipboard.Texts);
    }

    [Fact]
    public void CopyResultCommand_skips_clipboard_and_records_toast_when_empty()
    {
        RecordingClipboardWriter clipboard = new();
        ChatRunPage page = CreatePage(new ScriptedLlmChatClient(call => EmitChunks(call)), clipboard);

        InvokeCommand(page, "Copy result");

        Assert.Empty(clipboard.Texts);
        Assert.Equal("Nothing to copy yet", GetField<string?>(page, "_lastToastText"));
    }

    [Fact]
    public async Task CopyTranscriptCommand_copies_body_with_user_and_assistant_headers()
    {
        RecordingClipboardWriter clipboard = new();
        ChatRunPage page = CreatePage(new ScriptedLlmChatClient(call => EmitChunks(call, "done")), clipboard);

        StartStream(page, "hello");
        await GetStreamTask(page).WaitAsync(TimeSpan.FromSeconds(1));
        InvokeCommand(page, "Copy transcript");

        string copied = Assert.Single(clipboard.Texts);
        Assert.Contains("**You:**", copied, StringComparison.Ordinal);
        Assert.Contains("**Assistant:**", copied, StringComparison.Ordinal);
    }

    [Fact]
    public void StopCommand_with_no_active_stream_records_toast_without_throwing()
    {
        ChatRunPage page = CreatePage(new ScriptedLlmChatClient(call => EmitChunks(call)), new RecordingClipboardWriter());

        InvokeCommand(page, "Stop");

        Assert.Equal("Nothing to stop", GetField<string?>(page, "_lastToastText"));
    }

    [Fact]
    public void StopCommand_with_active_stream_cancels_cts()
    {
        ChatRunPage page = CreatePage(new ScriptedLlmChatClient(call => EmitChunks(call)), new RecordingClipboardWriter());
        using CancellationTokenSource cts = new();
        SetField(page, "_cts", cts);

        InvokeCommand(page, "Stop");

        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public async Task RerunCommand_clears_previous_state_before_second_stream()
    {
        int callIndex = 0;
        ScriptedLlmChatClient client = new(call => Interlocked.Increment(ref callIndex) == 1 ? EmitChunks(call, "first") : EmitChunks(call, "second"));
        ChatRunPage page = CreatePage(client, new RecordingClipboardWriter());

        SetField(page, "_initialUserMsg", "rerun prompt");
        StartStream(page, "first prompt");
        await GetStreamTask(page).WaitAsync(TimeSpan.FromSeconds(1));
        InvokeCommand(page, "Re-run");
        await GetStreamTask(page).WaitAsync(TimeSpan.FromSeconds(1));
        ChatRequest secondRequest = client.Requests[1];

        Assert.DoesNotContain(secondRequest.Messages, message => message.Role == ChatRole.Assistant && message.Content == "first");
        Assert.Collection(secondRequest.Messages,
            message => Assert.Equal(new ChatMessage(ChatRole.System, "You are concise."), message),
            message => Assert.Equal(new ChatMessage(ChatRole.User, "rerun prompt"), message));
        Assert.Equal("second", GetField<string>(page, "_lastAssistantText"));
    }

    [Fact]
    public void CopyResultCommand_reads_later_value_at_invoke_time()
    {
        RecordingClipboardWriter clipboard = new();
        ChatRunPage page = CreatePage(new ScriptedLlmChatClient(call => EmitChunks(call)), clipboard);
        CommandContextItem command = GetCommand(page, "Copy result");

        SetField(page, "_lastAssistantText", "late value");
        Invoke(command);

        Assert.Equal(["late value"], clipboard.Texts);
    }

    [Fact]
    public void Constructor_exposes_T35_run_page_commands()
    {
        ChatRunPage page = CreatePage(new ScriptedLlmChatClient(call => EmitChunks(call)), new RecordingClipboardWriter());

        Assert.Equal(["Copy result", "Copy transcript", "Stop", "Re-run", "Reply"], page.Commands.Cast<CommandContextItem>().Select(command => command.Title).ToArray());
    }

    private static ChatRunPage CreatePage(ILlmChatClient client, IClipboardWriter clipboard)
    {
        AgentDef agent = new("agent-a", "Agent A", "You are concise.", "Say foo.", "provider-a", "model-a", "\uE700", ["test"]);
        ProviderDef provider = new("provider-a", "Provider A", "https://provider.example.test/v1", "model-a");
        return new ChatRunPage(agent, provider, ApiKey, client, new RunSessionCoordinator(), clipboard, readClipboardAsync: () => Task.FromResult<string?>(null));
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

    private static void StartStream(ChatRunPage page, string message)
    {
        MethodInfo method = typeof(ChatRunPage).GetMethod("StartStream", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing StartStream.");
        method.Invoke(page, [message]);
    }

    private static Task GetStreamTask(ChatRunPage page) => GetField<Task?>(page, "_streamTask") ?? throw new InvalidOperationException("Missing stream task.");

    private static void InvokeCommand(ChatRunPage page, string title) => Invoke(GetCommand(page, title));

    private static CommandContextItem GetCommand(ChatRunPage page, string title)
    {
        return page.Commands.Cast<CommandContextItem>().Single(command => command.Title == title);
    }

    private static void Invoke(CommandContextItem item)
    {
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

    private static void SetField<T>(object target, string fieldName, T value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing field {fieldName}.");
        field.SetValue(target, value);
    }

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
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class RecordingClipboardWriter : IClipboardWriter
    {
        public List<string> Texts { get; } = new();

        public void SetText(string text) => Texts.Add(text);
    }
}
