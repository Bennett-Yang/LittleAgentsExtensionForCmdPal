using System.Reflection;
using System.Runtime.CompilerServices;
using LittleAgentsExtension.Llm;
using LittleAgentsExtension.Storage;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Xunit;

namespace LittleAgentsExtension.Tests;

public sealed class ChatRunPageInitialRunTests
{
    [Fact]
    public async Task GetContent_returns_preparing_output_immediately_when_clipboard_is_pending()
    {
        TaskCompletionSource<string?> clipboard = CreatePendingClipboard();
        ChatRunPage page = CreatePage("Use {input}", () => clipboard.Task);
        MarkdownContent output = GetField<MarkdownContent>(page, "_output");

        IContent[] content = page.GetContent();

        Assert.Same(output, Assert.Single(content));
        Assert.Equal("_Preparing..._", GetMarkdownBody(output));
        Task initTask = GetRequiredTask(page, "_initTask");
        Assert.False(initTask.IsCompleted);

        clipboard.SetResult(null);
        await initTask.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GetContent_starts_one_initialization_when_called_twice_before_clipboard_completes()
    {
        int clipboardCalls = 0;
        TaskCompletionSource<string?> clipboard = CreatePendingClipboard();
        TaskCompletionSource clipboardInvoked = CreateSignal();
        ChatRunPage page = CreatePage("Use {input}", () =>
        {
            clipboardCalls++;
            clipboardInvoked.SetResult();
            return clipboard.Task;
        });

        page.GetContent();
        Task? firstInitTask = GetField<Task?>(page, "_initTask");
        page.GetContent();

        await clipboardInvoked.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, clipboardCalls);
        Assert.Same(firstInitTask, GetField<Task?>(page, "_initTask"));

        if (firstInitTask is null)
        {
            throw new InvalidOperationException("Missing init task.");
        }

        clipboard.SetResult(null);
        await firstInitTask.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task InitializeFirstRunAsync_shows_input_form_without_starting_stream_when_template_requires_input()
    {
        ChatRunPage page = CreatePage("Use {input} with {selection}", () => Task.FromResult<string?>("selected text"));

        page.GetContent();
        await GetRequiredTask(page, "_initTask").WaitAsync(TimeSpan.FromSeconds(1));
        IContent[] content = page.GetContent();

        Assert.IsType<RunInputForm>(Assert.Single(content));
        Assert.True(GetField<bool>(page, "_showingInput"));
        Assert.Null(GetField<Task?>(page, "_streamTask"));
    }

    [Fact]
    public async Task SubmitForm_renders_initial_message_with_selection_and_marks_stream_started()
    {
        ChatRunPage page = CreatePage("Use {input} with {selection}", () => Task.FromResult<string?>("selected text"));

        page.GetContent();
        await GetRequiredTask(page, "_initTask").WaitAsync(TimeSpan.FromSeconds(1));
        RunInputForm form = Assert.IsType<RunInputForm>(Assert.Single(page.GetContent()));
        CommandResult result = form.SubmitForm("{\"Input\":\"typed text\"}");

        Assert.NotNull(result);
        Assert.Equal("Use typed text with selected text", GetField<string>(page, "_initialUserMsg"));
        Assert.False(GetField<bool>(page, "_showingInput"));
        Assert.Same(GetField<MarkdownContent>(page, "_output"), Assert.Single(page.GetContent()));
        Task streamTask = GetRequiredTask(page, "_streamTask");
        await streamTask.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(streamTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task InitializeFirstRunAsync_renders_message_and_marks_stream_started_when_template_has_no_input()
    {
        ChatRunPage page = CreatePage("Summarize {selection}", () => Task.FromResult<string?>("selected text"));

        page.GetContent();
        await GetRequiredTask(page, "_initTask").WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal("Summarize selected text", GetField<string>(page, "_initialUserMsg"));
        Assert.False(GetField<bool>(page, "_showingInput"));
        Assert.Same(GetField<MarkdownContent>(page, "_output"), Assert.Single(page.GetContent()));
        Assert.NotNull(GetField<Task?>(page, "_streamTask"));
    }

    [Fact]
    public async Task InitializeFirstRunAsync_treats_escaped_input_as_literal_without_showing_input_form()
    {
        ChatRunPage page = CreatePage("Literal {{input}} and {selection}", () => Task.FromResult<string?>("selected text"));

        page.GetContent();
        await GetRequiredTask(page, "_initTask").WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal("Literal {input} and selected text", GetField<string>(page, "_initialUserMsg"));
        Assert.False(GetField<bool>(page, "_showingInput"));
        Assert.NotNull(GetField<Task?>(page, "_streamTask"));
    }

    [Fact]
    public void GetContent_cancels_previous_active_page_before_input_is_submitted()
    {
        RunSessionCoordinator session = new();
        ChatRunPage firstPage = CreatePage("First", () => Task.FromResult<string?>(null), session);
        ChatRunPage secondPage = CreatePage("Use {input}", () => CreatePendingClipboard().Task, session);
        firstPage.GetContent();
        using CancellationTokenSource firstCancellation = new();
        SetField(firstPage, "_cts", firstCancellation);

        secondPage.GetContent();

        Assert.True(firstCancellation.IsCancellationRequested);
        Assert.True(session.IsActive(secondPage));
        Assert.Null(GetField<Task?>(secondPage, "_streamTask"));
    }

    private static ChatRunPage CreatePage(string userTemplate, Func<Task<string?>> readClipboardAsync, RunSessionCoordinator? session = null)
    {
        AgentDef agent = new("agent-a", "Agent A", "You are concise.", userTemplate, "provider-a", "model-a", "\uE700", ["test"]);
        ProviderDef provider = new("provider-a", "Provider A", "https://provider.example.test/v1", "model-a");
        return new ChatRunPage(agent, provider, "sk-test", new FakeLlmChatClient(), session ?? new RunSessionCoordinator(), new FakeClipboardWriter(), readClipboardAsync: readClipboardAsync);
    }

    private static TaskCompletionSource<string?> CreatePendingClipboard()
    {
        return new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static TaskCompletionSource CreateSignal()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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

    private static Task GetRequiredTask(object target, string fieldName)
    {
        return GetField<Task?>(target, fieldName) ?? throw new InvalidOperationException($"Missing task {fieldName}.");
    }

    private static string GetMarkdownBody(MarkdownContent content)
    {
        PropertyInfo body = typeof(MarkdownContent).GetProperty("Body")
            ?? throw new InvalidOperationException("MarkdownContent.Body is missing.");
        return (string)body.GetValue(content)!;
    }

    private sealed class FakeLlmChatClient : ILlmChatClient
    {
        public async IAsyncEnumerable<string> StreamAsync(ChatRequest request, ProviderDef provider, string apiKey, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class FakeClipboardWriter : IClipboardWriter
    {
        public void SetText(string text)
        {
        }
    }
}
