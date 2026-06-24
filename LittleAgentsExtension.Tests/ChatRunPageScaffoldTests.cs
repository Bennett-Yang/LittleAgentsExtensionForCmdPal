using System.Reflection;
using System.Runtime.CompilerServices;
using LittleAgentsExtension.Llm;
using LittleAgentsExtension.Storage;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Xunit;

namespace LittleAgentsExtension.Tests;

public sealed class ChatRunPageScaffoldTests
{
    [Fact]
    public void Constructor_initializes_title_icon_output_and_empty_session_state()
    {
        RunSessionCoordinator session = new();
        ChatRunPage page = CreatePage(session, icon: "\uE777");

        Assert.Equal("Agent A", page.Title);
        AssertObjectContainsString(page.Icon, "\uE777");
        Assert.Equal(string.Empty, GetMarkdownBody(GetField<MarkdownContent>(page, "_output")));
        Assert.Empty(GetField<List<ChatMessage>>(page, "_history"));
        Assert.Equal(string.Empty, GetField<string>(page, "_lastAssistantText"));
        Assert.Equal(string.Empty, GetField<string>(page, "_initialUserMsg"));
        Assert.Equal(RuntimeSettings.Default, GetField<Func<RuntimeSettings>>(page, "_settings")());
    }

    [Fact]
    public void Constructor_uses_default_agent_icon_when_agent_icon_is_missing()
    {
        ChatRunPage page = CreatePage(new RunSessionCoordinator(), icon: null);

        AssertObjectContainsString(page.Icon, "\uE945");
    }

    [Fact]
    public void GetContent_returns_output_content_and_activates_session_once()
    {
        RunSessionCoordinator session = new();
        TaskCompletionSource<string?> clipboard = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ChatRunPage page = CreatePage(session, readClipboardAsync: () => clipboard.Task);
        MarkdownContent output = GetField<MarkdownContent>(page, "_output");

        IContent[] firstContent = page.GetContent();
        using CancellationTokenSource pageCancellation = new();
        SetField(page, "_cts", pageCancellation);
        IContent[] secondContent = page.GetContent();

        Assert.Same(output, Assert.Single(firstContent));
        Assert.Same(output, Assert.Single(secondContent));
        Assert.True(session.IsActive(page));
        Assert.False(pageCancellation.IsCancellationRequested);
    }

    [Fact]
    public void Activating_second_page_cancels_previous_page_and_updates_active_page()
    {
        RunSessionCoordinator session = new();
        ChatRunPage firstPage = CreatePage(session, agentName: "Agent A");
        ChatRunPage secondPage = CreatePage(session, agentName: "Agent B");
        firstPage.GetContent();
        using CancellationTokenSource firstCancellation = new();
        SetField(firstPage, "_cts", firstCancellation);

        secondPage.GetContent();

        Assert.True(firstCancellation.IsCancellationRequested);
        Assert.False(session.IsActive(firstPage));
        Assert.True(session.IsActive(secondPage));
    }

    private static ChatRunPage CreatePage(RunSessionCoordinator session, string agentName = "Agent A", string? icon = "\uE700", Func<Task<string?>>? readClipboardAsync = null)
    {
        AgentDef agent = new("agent-a", agentName, "You are concise.", "Say foo.", "provider-a", "model-a", icon, ["test"]);
        ProviderDef provider = new("provider-a", "Provider A", "https://provider.example.test/v1", "model-a");
        return new ChatRunPage(agent, provider, "sk-test", new FakeLlmChatClient(), session, new FakeClipboardWriter(), readClipboardAsync: readClipboardAsync ?? (() => Task.FromResult<string?>(null)));
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

    private static string GetMarkdownBody(MarkdownContent content)
    {
        PropertyInfo body = typeof(MarkdownContent).GetProperty("Body")
            ?? throw new InvalidOperationException("MarkdownContent.Body is missing.");
        return (string)body.GetValue(content)!;
    }

    private static void AssertObjectContainsString(object? target, string expected)
    {
        Assert.NotNull(target);
        Assert.True(ContainsString(target, expected), $"Expected {target.GetType().FullName} to contain '{expected}'.");
    }

    private static bool ContainsString(object target, string expected)
    {
        return ContainsString(target, expected, new HashSet<object>(ReferenceEqualityComparer.Instance), depth: 0);
    }

    private static bool ContainsString(object? target, string expected, HashSet<object> visited, int depth)
    {
        if (target is null || depth > 4)
        {
            return false;
        }

        if (target is string text)
        {
            return text == expected;
        }

        if (!visited.Add(target))
        {
            return false;
        }

        Type targetType = target.GetType();
        if (targetType.IsPrimitive || targetType.IsEnum)
        {
            return false;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (PropertyInfo property in targetType.GetProperties(flags).Where(property => property.GetIndexParameters().Length == 0))
        {
            if (ContainsString(property.GetValue(target), expected, visited, depth + 1))
            {
                return true;
            }
        }

        foreach (FieldInfo field in targetType.GetFields(flags))
        {
            if (ContainsString(field.GetValue(target), expected, visited, depth + 1))
            {
                return true;
            }
        }

        return false;
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
