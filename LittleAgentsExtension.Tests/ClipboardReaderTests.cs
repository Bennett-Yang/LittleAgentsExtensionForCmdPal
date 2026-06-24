using LittleAgentsExtension.Llm;
using Xunit;

namespace LittleAgentsExtension.Tests;

public sealed class ClipboardReaderTests
{
    [Fact]
    public async Task TryGetTextAsync_returns_text_when_available()
    {
        FakeClipboardContent content = new(true, "clipboard text");
        FakeClipboardSource source = new(content);
        ClipboardReader reader = new(source);

        string? result = await reader.TryGetTextAsync();

        Assert.Equal("clipboard text", result);
        Assert.Equal(1, content.GetTextCalls);
        Assert.Equal(0, source.FallbackCalls);
    }

    [Fact]
    public async Task TryGetTextAsync_returns_null_for_image_only_clipboard()
    {
        FakeClipboardContent content = new(false, null);
        FakeClipboardSource source = new(content);
        ClipboardReader reader = new(source);

        string? result = await reader.TryGetTextAsync();

        Assert.Null(result);
        Assert.Equal(0, content.GetTextCalls);
        Assert.Equal(0, source.FallbackCalls);
    }

    [Fact]
    public async Task TryGetTextAsync_returns_null_for_empty_clipboard()
    {
        FakeClipboardSource source = new(null);
        ClipboardReader reader = new(source);

        string? result = await reader.TryGetTextAsync();

        Assert.Null(result);
        Assert.Equal(0, source.FallbackCalls);
    }

    private sealed class FakeClipboardSource : IClipboardSource
    {
        private readonly IClipboardContent? _content;

        public FakeClipboardSource(IClipboardContent? content)
        {
            _content = content;
        }

        public int FallbackCalls { get; private set; }

        public ValueTask<IClipboardContent?> GetContentAsync()
        {
            return ValueTask.FromResult(_content);
        }

        public ValueTask<string?> TryGetFallbackTextAsync()
        {
            FallbackCalls++;
            return ValueTask.FromResult<string?>(null);
        }
    }

    private sealed class FakeClipboardContent : IClipboardContent
    {
        private readonly string? _text;

        public FakeClipboardContent(bool containsText, string? text)
        {
            ContainsText = containsText;
            _text = text;
        }

        public bool ContainsText { get; }

        public int GetTextCalls { get; private set; }

        public ValueTask<string?> GetTextAsync()
        {
            GetTextCalls++;
            return ValueTask.FromResult(_text);
        }
    }
}
